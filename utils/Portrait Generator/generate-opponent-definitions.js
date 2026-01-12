const fs = require("fs");
const path = require("path");

// Configuration
const PROMPTS_FILE = path.join(__dirname, "opponent_prompts.json");
const OUTPUT_FILE = path.join(__dirname, "opponent_definitions.json");

/**
 * Generate random integer between min (inclusive) and max (inclusive)
 */
function randomInt(min, max) {
  return Math.floor(Math.random() * (max - min + 1)) + min;
}

/**
 * Clamp skill to valid range (80-100)
 */
function clampSkill(value) {
  return Math.max(80, Math.min(100, value));
}

/**
 * Clamp aggression to valid range (0-100)
 */
function clampAggression(value) {
  return Math.max(0, Math.min(100, value));
}

/**
 * Calculate age-based aggression modifier
 * Matches OpponentGenerationService.cs logic exactly
 *
 * Young drivers (18-25): +10 to +20 aggression
 * Middle-aged drivers (26-45): 0 to +5 aggression
 * Older drivers (46+): -10 to -20 aggression
 */
function calculateAgeModifier(age) {
  if (age < 26) {
    // Young hotshots: more aggressive
    return randomInt(10, 20);
  } else if (age < 46) {
    // Middle-aged: neutral with slight variation
    return randomInt(0, 5);
  } else {
    // Older veterans: more cautious
    return randomInt(-20, -10);
  }
}

/**
 * Calculate gender-based aggression modifier (subtle tendency)
 * Matches OpponentGenerationService.cs logic exactly
 *
 * This is a statistical tendency, not a hard rule - allows for individual variation
 * Male: slight positive tendency (average +2)
 * Female: slight negative tendency (average -1)
 * Other: neutral
 */
function calculateGenderModifier(gender) {
  const genderLower = gender.toLowerCase();

  if (genderLower === "male") {
    return randomInt(-2, 5);  // Slight positive tendency (average +2)
  } else if (genderLower === "female") {
    return randomInt(-5, 2);  // Slight negative tendency (average -1)
  } else {
    return randomInt(-3, 3);  // Neutral
  }
}

/**
 * Generate skill and aggression for an opponent
 * Matches OpponentGenerationService.cs logic exactly
 */
function generateTraits(age, gender) {
  // Generate base skill between 80-100
  const skill = randomInt(80, 100);

  // Generate base aggression (30-70 for neutral range)
  const baseAggression = randomInt(30, 70);

  // Apply age modifier
  const ageModifier = calculateAgeModifier(age);

  // Apply gender tendency (subtle variation, not a hard rule)
  const genderModifier = calculateGenderModifier(gender);

  // Calculate final aggression with modifiers
  let aggression = baseAggression + ageModifier + genderModifier;

  // Clamp to valid range (0-100)
  aggression = clampAggression(aggression);

  return {
    skill: clampSkill(skill),
    aggression,
    baseAggression,
    ageModifier,
    genderModifier
  };
}

/**
 * Map gender string to proper case for C# enum
 */
function normalizeGender(gender) {
  const genderLower = gender.toLowerCase();
  if (genderLower === "male") return "Male";
  if (genderLower === "female") return "Female";
  return "Other";
}

/**
 * Generate location flavor text based on nickname/character
 */
function generateLocation(driver) {
  const locations = [
    "Downtown Garage District",
    "East Side Auto Shop",
    "West End Speed Shop",
    "Industrial Quarter",
    "Riverside Mechanics",
    "North Side Garage",
    "South End Workshop",
    "Main Street Motors",
    "Backstreet Garage",
    "Highway 66 Shop"
  ];

  // Deterministic selection based on driver ID
  const index = parseInt(driver.id.replace("drv_", "")) % locations.length;
  return locations[index];
}

/**
 * Generate biography flavor text
 */
function generateBiography(driver, traits) {
  const templates = {
    young_aggressive: "A young hotshot with a need for speed and something to prove on the streets.",
    young_moderate: "An up-and-coming racer making a name in the local street racing scene.",
    middle_aggressive: "An experienced racer who drives hard and takes calculated risks.",
    middle_moderate: "A seasoned driver with solid racing skills and consistent performance.",
    veteran_cautious: "A street racing veteran who knows when to push and when to hold back.",
    veteran_experienced: "An old-school racer with years of experience and hard-earned wisdom."
  };

  let category;
  if (driver.age < 26) {
    category = traits.aggression > 65 ? "young_aggressive" : "young_moderate";
  } else if (driver.age < 46) {
    category = traits.aggression > 60 ? "middle_aggressive" : "middle_moderate";
  } else {
    category = traits.aggression > 50 ? "veteran_experienced" : "veteran_cautious";
  }

  return templates[category];
}

/**
 * Generate opponent definition from prompt data
 */
function generateOpponentDefinition(driver) {
  const traits = generateTraits(driver.age, driver.gender);
  const location = generateLocation(driver);
  const biography = generateBiography(driver, traits);

  return {
    opponentId: driver.id,
    name: driver.name,
    nickname: driver.nickname,
    age: driver.age,
    gender: normalizeGender(driver.gender),
    skill: traits.skill,
    aggression: traits.aggression,
    portraitPath: `/Assets/Opponents/${driver.id}.png`,
    location: location,
    biography: biography,
    _generationDetails: {
      baseAggression: traits.baseAggression,
      ageModifier: traits.ageModifier,
      genderModifier: traits.genderModifier,
      finalAggression: traits.aggression
    }
  };
}

/**
 * Main function
 */
function main() {
  console.log("🎮 Street Rod Opponent Definitions Generator\n");
  console.log("=".repeat(60));

  // Load opponent prompts
  console.log("\n📄 Loading opponent prompts...");
  if (!fs.existsSync(PROMPTS_FILE)) {
    console.error(`\n⚠️  Prompts file not found: ${PROMPTS_FILE}`);
    process.exit(1);
  }

  const promptData = JSON.parse(fs.readFileSync(PROMPTS_FILE, "utf8"));
  const drivers = promptData.drivers;

  console.log(`   Loaded ${drivers.length} opponent prompts`);

  // Generate opponent definitions
  console.log("\n⚙️  Generating opponent definitions...\n");

  const opponents = drivers.map((driver, index) => {
    const opponent = generateOpponentDefinition(driver);

    console.log(`[${(index + 1).toString().padStart(3)}/${drivers.length}] ${opponent.name} "${opponent.nickname}"`);
    console.log(`   Age: ${opponent.age} | Gender: ${opponent.gender}`);
    console.log(`   Skill: ${opponent.skill} | Aggression: ${opponent.aggression}`);
    console.log(`   Modifiers: Base=${opponent._generationDetails.baseAggression}, Age=${opponent._generationDetails.ageModifier > 0 ? '+' : ''}${opponent._generationDetails.ageModifier}, Gender=${opponent._generationDetails.genderModifier > 0 ? '+' : ''}${opponent._generationDetails.genderModifier}`);
    console.log(`   Location: ${opponent.location}`);
    console.log();

    return opponent;
  });

  // Create output structure
  const output = {
    version: "1.0",
    description: "Street Rod AC - Opponent Definitions",
    generated: new Date().toISOString(),
    generation_rules: {
      skill_range: "80-100",
      aggression_range: "0-100",
      base_aggression_range: "30-70",
      age_modifiers: {
        young: "18-25 years: +10 to +20 aggression",
        middle: "26-45 years: 0 to +5 aggression",
        veteran: "46+ years: -10 to -20 aggression"
      },
      gender_modifiers: {
        male: "slight positive tendency (-2 to +5, avg +2)",
        female: "slight negative tendency (-5 to +2, avg -1)",
        other: "neutral (-3 to +3)"
      }
    },
    opponents: opponents
  };

  // Write output file
  console.log("=".repeat(60));
  console.log("\n💾 Saving opponent definitions...");
  fs.writeFileSync(OUTPUT_FILE, JSON.stringify(output, null, 2), "utf8");
  console.log(`   ✓ Saved to: ${OUTPUT_FILE}`);

  // Statistics
  console.log("\n📊 Generation Statistics:\n");

  const avgSkill = opponents.reduce((sum, o) => sum + o.skill, 0) / opponents.length;
  const avgAggression = opponents.reduce((sum, o) => sum + o.aggression, 0) / opponents.length;

  const youngDrivers = opponents.filter(o => o.age < 26);
  const middleDrivers = opponents.filter(o => o.age >= 26 && o.age < 46);
  const veteranDrivers = opponents.filter(o => o.age >= 46);

  const maleDrivers = opponents.filter(o => o.gender === "Male");
  const femaleDrivers = opponents.filter(o => o.gender === "Female");

  console.log(`   Total Opponents: ${opponents.length}`);
  console.log(`   Average Skill: ${avgSkill.toFixed(1)}`);
  console.log(`   Average Aggression: ${avgAggression.toFixed(1)}`);
  console.log();
  console.log(`   Age Distribution:`);
  console.log(`     Young (18-25): ${youngDrivers.length} drivers, avg aggression: ${(youngDrivers.reduce((sum, o) => sum + o.aggression, 0) / youngDrivers.length).toFixed(1)}`);
  console.log(`     Middle (26-45): ${middleDrivers.length} drivers, avg aggression: ${(middleDrivers.reduce((sum, o) => sum + o.aggression, 0) / middleDrivers.length).toFixed(1)}`);
  console.log(`     Veteran (46+): ${veteranDrivers.length} drivers, avg aggression: ${(veteranDrivers.reduce((sum, o) => sum + o.aggression, 0) / veteranDrivers.length).toFixed(1)}`);
  console.log();
  console.log(`   Gender Distribution:`);
  console.log(`     Male: ${maleDrivers.length} drivers, avg aggression: ${(maleDrivers.reduce((sum, o) => sum + o.aggression, 0) / maleDrivers.length).toFixed(1)}`);
  console.log(`     Female: ${femaleDrivers.length} drivers, avg aggression: ${(femaleDrivers.reduce((sum, o) => sum + o.aggression, 0) / femaleDrivers.length).toFixed(1)}`);

  console.log("\n" + "=".repeat(60));
  console.log("\n✅ Opponent definitions generated successfully!\n");
  console.log("📌 IMPORTANT: These definitions follow the opponent system guidelines:");
  console.log("   - Engine-agnostic (no AC-specific values)");
  console.log("   - Skill range: 80-100");
  console.log("   - Aggression range: 0-100");
  console.log("   - Age-based modifiers applied");
  console.log("   - Gender as subtle tendency (not hard rule)");
  console.log("   - Ready for persistence in LiteDB");
  console.log("\n" + "=".repeat(60) + "\n");
}

// Run the script
if (require.main === module) {
  main();
}

module.exports = { generateOpponentDefinition, generateTraits };
