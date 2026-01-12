const fs = require("fs");
const path = require("path");
const http = require("http");

// Configuration
const COMFYUI_HOST = "127.0.0.1";
const COMFYUI_PORT = 8000;
const OUTPUT_DIR = __dirname; // Same directory as the script
const PROMPTS_FILE = path.join(__dirname, "opponent_prompts.json");
const WORKFLOW_FILE = path.join(__dirname, "comfyui-workflow.json");

/**
 * Load opponent prompts from JSON
 */
function loadOpponentPrompts() {
  if (!fs.existsSync(PROMPTS_FILE)) {
    console.error("\n⚠️  Opponent prompts file not found!");
    console.error(`Expected location: ${PROMPTS_FILE}`);
    process.exit(1);
  }

  const data = JSON.parse(fs.readFileSync(PROMPTS_FILE, "utf8"));
  return data;
}

/**
 * Queue a prompt in ComfyUI
 */
function queuePrompt(workflow) {
  return new Promise((resolve, reject) => {
    const data = JSON.stringify({ prompt: workflow });

    const options = {
      hostname: COMFYUI_HOST,
      port: COMFYUI_PORT,
      path: "/prompt",
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "Content-Length": data.length,
      },
    };

    const req = http.request(options, (res) => {
      let body = "";
      res.on("data", (chunk) => (body += chunk));
      res.on("end", () => {
        if (res.statusCode === 200) {
          resolve(JSON.parse(body));
        } else {
          reject(new Error(`HTTP ${res.statusCode}: ${body}`));
        }
      });
    });

    req.on("error", reject);
    req.write(data);
    req.end();
  });
}

/**
 * Get workflow status
 */
function getHistory(promptId) {
  return new Promise((resolve, reject) => {
    const options = {
      hostname: COMFYUI_HOST,
      port: COMFYUI_PORT,
      path: `/history/${promptId}`,
      method: "GET",
    };

    const req = http.request(options, (res) => {
      let body = "";
      res.on("data", (chunk) => (body += chunk));
      res.on("end", () => {
        if (res.statusCode === 200) {
          resolve(JSON.parse(body));
        } else {
          reject(new Error(`HTTP ${res.statusCode}: ${body}`));
        }
      });
    });

    req.on("error", reject);
    req.end();
  });
}

/**
 * Download generated image
 */
function downloadImage(filename, subfolder, type) {
  return new Promise((resolve, reject) => {
    const params = new URLSearchParams({ filename, subfolder, type });

    const options = {
      hostname: COMFYUI_HOST,
      port: COMFYUI_PORT,
      path: `/view?${params.toString()}`,
      method: "GET",
    };

    const req = http.request(options, (res) => {
      if (res.statusCode !== 200) {
        reject(new Error(`HTTP ${res.statusCode}`));
        return;
      }

      const chunks = [];
      res.on("data", (chunk) => chunks.push(chunk));
      res.on("end", () => resolve(Buffer.concat(chunks)));
    });

    req.on("error", reject);
    req.end();
  });
}

/**
 * Wait for workflow to complete
 */
async function waitForCompletion(promptId, timeout = 300000) {
  const startTime = Date.now();

  while (Date.now() - startTime < timeout) {
    await new Promise((resolve) => setTimeout(resolve, 1000));

    try {
      const history = await getHistory(promptId);

      if (history[promptId]) {
        const status = history[promptId].status;

        if (status && status.completed) {
          return history[promptId];
        }

        if (status && status.status_str === "error") {
          throw new Error("Workflow execution failed");
        }
      }
    } catch (err) {
      // History might not be available yet, continue waiting
    }
  }

  throw new Error("Timeout waiting for workflow completion");
}

/**
 * Load and modify workflow for a specific driver
 */
function loadWorkflow(driver, negativePrompt) {
  if (!fs.existsSync(WORKFLOW_FILE)) {
    console.error("\n⚠️  Workflow file not found!");
    console.error(
      "Please export your ComfyUI workflow as JSON and save it to:"
    );
    console.error(`   ${WORKFLOW_FILE}`);
    console.error(
      "\nIn ComfyUI: Settings → Save (API Format) → comfyui-workflow.json\n"
    );
    process.exit(1);
  }

  const workflow = JSON.parse(fs.readFileSync(WORKFLOW_FILE, "utf8"));

  // Find positive and negative prompt nodes
  let positivePromptNode = null;
  let negativePromptNode = null;

  // First pass: identify nodes by their content or title
  for (const [nodeId, node] of Object.entries(workflow)) {
    if (
      node.class_type === "CLIPTextEncode" &&
      node.inputs &&
      node.inputs.text !== undefined
    ) {
      const currentText = String(node.inputs.text).toLowerCase();
      const title = String(node._meta?.title || "").toLowerCase();

      // Heuristics to identify negative prompt
      if (
        currentText.includes("negative") ||
        currentText.includes("bad quality") ||
        currentText.includes("low quality") ||
        currentText.includes("worst quality") ||
        currentText.includes("modern") ||
        title.includes("negative")
      ) {
        negativePromptNode = nodeId;
      } else if (!positivePromptNode) {
        // First non-negative CLIPTextEncode is likely the positive prompt
        positivePromptNode = nodeId;
      }
    }
  }

  // If we couldn't identify nodes, use first two CLIPTextEncode nodes
  if (!positivePromptNode || !negativePromptNode) {
    const clipNodes = Object.entries(workflow)
      .filter(([, node]) => node.class_type === "CLIPTextEncode")
      .map(([id]) => id);

    if (clipNodes.length >= 2) {
      positivePromptNode = positivePromptNode || clipNodes[0];
      negativePromptNode = negativePromptNode || clipNodes[1];
    }
  }

  // Update the prompts
  if (positivePromptNode && workflow[positivePromptNode]) {
    workflow[positivePromptNode].inputs.text = driver.prompt;
  }

  if (negativePromptNode && workflow[negativePromptNode]) {
    workflow[negativePromptNode].inputs.text = negativePrompt;
  }

  // Update seed for variation in all KSampler nodes
  for (const [, node] of Object.entries(workflow)) {
    if (
      node.class_type === "KSampler" &&
      node.inputs &&
      node.inputs.seed !== undefined
    ) {
      node.inputs.seed = Math.floor(Math.random() * 1000000000);
    }
  }

  // Update filename prefix in SaveImage node
  for (const [, node] of Object.entries(workflow)) {
    if (
      node.class_type === "SaveImage" &&
      node.inputs &&
      node.inputs.filename_prefix !== undefined
    ) {
      node.inputs.filename_prefix = driver.name;
    }
  }

  return workflow;
}

/**
 * Generate portrait for a single driver
 */
async function generateDriverPortrait(driver, index, total, negativePrompt) {
  console.log(`\n[${index}/${total}] Generating portrait for: ${driver.name} "${driver.nickname}"`);
  console.log(`  🆔 ID: ${driver.id}`);
  console.log(`  👤 ${driver.gender} | Age: ${driver.age}`);

  // Check if portrait already exists
  const outputPath = path.join(OUTPUT_DIR, `${driver.name}.png`);
  if (fs.existsSync(outputPath)) {
    console.log(`  ✓ Portrait already exists, skipping...`);
    return;
  }

  try {
    // Load and customize workflow
    const workflow = loadWorkflow(driver, negativePrompt);
    console.log(`  📝 Prompt: ${driver.prompt.substring(0, 80)}...`);

    // Queue the prompt
    console.log("  → Queuing workflow...");
    const result = await queuePrompt(workflow);
    const promptId = result.prompt_id;
    console.log(`  → Prompt ID: ${promptId}`);

    // Wait for completion
    console.log("  → Waiting for generation...");
    const history = await waitForCompletion(promptId);

    // Find the output image
    const outputs = history.outputs;
    let imageData = null;

    for (const [, output] of Object.entries(outputs)) {
      if (output.images && output.images.length > 0) {
        const image = output.images[0];
        console.log("  → Downloading image...");
        imageData = await downloadImage(
          image.filename,
          image.subfolder,
          image.type
        );
        break;
      }
    }

    if (!imageData) {
      throw new Error("No output image found");
    }

    // Save the image
    fs.writeFileSync(outputPath, imageData);
    console.log(`  ✓ Saved to: ${outputPath}`);
  } catch (err) {
    console.error(`  ✗ Error: ${err.message}`);
    throw err;
  }
}

/**
 * Main function
 */
async function main() {
  console.log("🎨 Street Rod Opponent Portrait Generator\n");
  console.log("=".repeat(60));

  // Load opponent prompts
  console.log("\n📄 Loading opponent prompts...");
  const promptData = loadOpponentPrompts();
  const drivers = promptData.drivers;
  const negativePrompt = promptData.negative_prompt;

  console.log(`   Loaded ${drivers.length} opponent definitions`);
  console.log(`   Style Profile: ${promptData.style_profile}`);

  // List drivers to process
  console.log("\nOpponents to process:");
  drivers.forEach((driver, i) => {
    console.log(
      `  ${(i + 1).toString().padStart(3)}. [${driver.id}] ${driver.name.padEnd(
        20
      )} "${driver.nickname}" (${driver.gender}, ${driver.age})`
    );
  });

  console.log("\n" + "=".repeat(60));
  console.log("\n🚀 Starting generation process...\n");

  let successful = 0;
  let failed = 0;
  const failedDrivers = [];

  for (let i = 0; i < drivers.length; i++) {
    try {
      await generateDriverPortrait(
        drivers[i],
        i + 1,
        drivers.length,
        negativePrompt
      );
      successful++;

      // Small delay between generations
      if (i < drivers.length - 1) {
        await new Promise((resolve) => setTimeout(resolve, 500));
      }
    } catch (err) {
      failed++;
      failedDrivers.push(drivers[i].name);
    }
  }

  // Summary
  console.log("\n" + "=".repeat(60));
  console.log("\n📊 Generation Summary:\n");
  console.log(`  ✓ Successful: ${successful}`);
  console.log(`  ✗ Failed: ${failed}`);

  if (failedDrivers.length > 0) {
    console.log("\n  Failed drivers:");
    failedDrivers.forEach((driver) => console.log(`    - ${driver}`));
  }

  console.log(`\n  📁 Output directory: ${OUTPUT_DIR}`);
  console.log("\n" + "=".repeat(60) + "\n");
}

// Run the script
if (require.main === module) {
  main().catch((err) => {
    console.error("\n❌ Fatal error:", err);
    process.exit(1);
  });
}

module.exports = { loadOpponentPrompts, generateDriverPortrait };
