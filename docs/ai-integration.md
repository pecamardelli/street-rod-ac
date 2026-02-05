# AI Integration System

## Overview

Street Rod AC integrates with local Large Language Models (LLMs) via Ollama or LM Studio to provide dynamic, contextual content generation. This is an **optional** feature - the game functions fully without AI, falling back to static/procedural content.

## Vision

AI integration enhances immersion by making the game world feel more alive and responsive:

| Feature | Without AI | With AI |
|---------|------------|---------|
| Opponent Dialogue | Preset phrases based on stats | Contextual, personality-driven responses |
| Car Improvement | Fixed upgrade paths | Personalized recommendations based on player style |
| Opponent Creation | Random stat generation | Coherent backstories and personalities |
| Race Commentary | None | Dynamic race narration |
| Newspaper Articles | Template-based | Generated stories about player achievements |

## Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Application Layer                             │
│  ┌─────────────┐  ┌─────────────────┐  ┌──────────────────────────┐    │
│  │ TalkService │  │ MechanicService │  │ OpponentCreationService  │    │
│  └──────┬──────┘  └────────┬────────┘  └────────────┬─────────────┘    │
│         │                  │                        │                   │
│         └──────────────────┼────────────────────────┘                   │
│                            │                                            │
│                    ┌───────▼───────┐                                    │
│                    │ IAiService    │                                    │
│                    │ (Interface)   │                                    │
│                    └───────┬───────┘                                    │
│                            │                                            │
│         ┌──────────────────┼──────────────────┐                        │
│         │                  │                  │                        │
│  ┌──────▼──────┐   ┌───────▼───────┐  ┌──────▼──────┐                  │
│  │ NullAiService│   │ OllamaService │  │LmStudioService│                │
│  │ (Fallback)  │   │               │  │              │                  │
│  └─────────────┘   └───────┬───────┘  └──────┬──────┘                  │
│                            │                  │                        │
└────────────────────────────┼──────────────────┼────────────────────────┘
                             │                  │
                    ┌────────▼──────────────────▼────────┐
                    │         Local LLM Runtime          │
                    │  ┌─────────┐      ┌─────────────┐  │
                    │  │ Ollama  │      │  LM Studio  │  │
                    │  │ :11434  │      │   :1234     │  │
                    │  └─────────┘      └─────────────┘  │
                    └────────────────────────────────────┘
```

## Core Interface

```csharp
// Services/AI/IAiService.cs

public interface IAiService
{
    /// <summary>
    /// Check if AI service is available and responding
    /// </summary>
    Task<bool> IsAvailableAsync();

    /// <summary>
    /// Generate text completion from a prompt
    /// </summary>
    Task<AiResponse> GenerateAsync(AiRequest request);

    /// <summary>
    /// Get available models from the service
    /// </summary>
    Task<List<string>> GetAvailableModelsAsync();

    /// <summary>
    /// Service configuration
    /// </summary>
    AiServiceConfig Config { get; }
}

public class AiRequest
{
    /// <summary>
    /// System prompt defining the AI's role and constraints
    /// </summary>
    public string SystemPrompt { get; set; }

    /// <summary>
    /// User prompt with the specific request
    /// </summary>
    public string UserPrompt { get; set; }

    /// <summary>
    /// Maximum tokens to generate (default: 150)
    /// </summary>
    public int MaxTokens { get; set; } = 150;

    /// <summary>
    /// Temperature for randomness (0.0 = deterministic, 1.0 = creative)
    /// </summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>
    /// Request category for logging/metrics
    /// </summary>
    public AiRequestCategory Category { get; set; }
}

public class AiResponse
{
    public bool Success { get; set; }
    public string Content { get; set; }
    public string Error { get; set; }
    public TimeSpan GenerationTime { get; set; }
    public int TokensUsed { get; set; }
}

public enum AiRequestCategory
{
    OpponentDialogue,
    MechanicAdvice,
    OpponentCreation,
    RaceCommentary,
    NewspaperArticle
}

public class AiServiceConfig
{
    public string ServiceType { get; set; }  // "ollama", "lmstudio", "custom"
    public string Endpoint { get; set; }      // "http://localhost:11434"
    public string ModelId { get; set; }       // "llama3.2:3b"
    public int TimeoutSeconds { get; set; } = 30;
}
```

## Implementations

### NullAiService (Fallback)

Always returns `IsAvailable = false`, forcing callers to use fallback logic. Used when AI is disabled or unavailable.

```csharp
public class NullAiService : IAiService
{
    public Task<bool> IsAvailableAsync() => Task.FromResult(false);

    public Task<AiResponse> GenerateAsync(AiRequest request)
    {
        return Task.FromResult(new AiResponse
        {
            Success = false,
            Error = "AI service not configured"
        });
    }

    public Task<List<string>> GetAvailableModelsAsync()
        => Task.FromResult(new List<string>());

    public AiServiceConfig Config => new() { ServiceType = "none" };
}
```

### OllamaService

Connects to Ollama's REST API.

```csharp
public class OllamaService : IAiService
{
    private readonly HttpClient _httpClient;
    private readonly AiServiceConfig _config;
    private readonly IAppLogger _logger;

    public OllamaService(AiServiceConfig config, IAppLogger logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.Endpoint),
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
        };
    }

    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<AiResponse> GenerateAsync(AiRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var payload = new
            {
                model = _config.ModelId,
                prompt = request.UserPrompt,
                system = request.SystemPrompt,
                stream = false,
                options = new
                {
                    temperature = request.Temperature,
                    num_predict = request.MaxTokens
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/generate", content);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI", $"Ollama error: {response.StatusCode}");
                return new AiResponse { Success = false, Error = responseJson };
            }

            var result = JsonSerializer.Deserialize<OllamaResponse>(responseJson);

            stopwatch.Stop();

            _logger.LogInfo("AI", $"Generated {request.Category} in {stopwatch.ElapsedMilliseconds}ms");

            return new AiResponse
            {
                Success = true,
                Content = result.Response?.Trim() ?? "",
                GenerationTime = stopwatch.Elapsed,
                TokensUsed = result.EvalCount
            };
        }
        catch (TaskCanceledException)
        {
            return new AiResponse { Success = false, Error = "Request timed out" };
        }
        catch (Exception ex)
        {
            _logger.LogError("AI", $"Generation failed: {ex.Message}");
            return new AiResponse { Success = false, Error = ex.Message };
        }
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/tags");
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OllamaTagsResponse>(json);
            return result.Models?.Select(m => m.Name).ToList() ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public AiServiceConfig Config => _config;

    // Response DTOs
    private class OllamaResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; }

        [JsonPropertyName("eval_count")]
        public int EvalCount { get; set; }
    }

    private class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModel> Models { get; set; }
    }

    private class OllamaModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }
}
```

### LmStudioService

Similar to Ollama but uses OpenAI-compatible API format.

```csharp
public class LmStudioService : IAiService
{
    // LM Studio exposes OpenAI-compatible API at /v1/chat/completions
    // Implementation follows OpenAI chat format

    public async Task<AiResponse> GenerateAsync(AiRequest request)
    {
        var payload = new
        {
            model = _config.ModelId,
            messages = new[]
            {
                new { role = "system", content = request.SystemPrompt },
                new { role = "user", content = request.UserPrompt }
            },
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            stream = false
        };

        // POST to /v1/chat/completions
        // Parse response.choices[0].message.content
    }
}
```

---

## Feature Implementations

### 1. Opponent Dialogue (Talk Service)

Generates contextual dialogue when player interacts with opponents.

```csharp
// Services/Talk/AiTalkService.cs

public class AiTalkService : ITalkService
{
    private readonly IAiService _aiService;
    private readonly StaticTalkService _fallback;

    public async Task<string> GetMessageAsync(TalkContext context)
    {
        if (!await _aiService.IsAvailableAsync())
            return await _fallback.GetMessageAsync(context);

        var systemPrompt = BuildSystemPrompt(context);
        var userPrompt = BuildUserPrompt(context);

        var response = await _aiService.GenerateAsync(new AiRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            MaxTokens = 100,
            Temperature = 0.8f,
            Category = AiRequestCategory.OpponentDialogue
        });

        if (response.Success && !string.IsNullOrWhiteSpace(response.Content))
            return CleanDialogue(response.Content);

        return await _fallback.GetMessageAsync(context);
    }

    private string BuildSystemPrompt(TalkContext context)
    {
        var opponent = context.Opponent;
        var personality = GetPersonalityDescription(opponent.Aggression);

        return $@"You are {opponent.Name}, nicknamed ""{opponent.Nickname}"".
You are a street racer in 1960s Los Angeles.
Your reputation score is {opponent.Stats.Reputation} (scale: 0-100).
Your racing record: {opponent.Stats.Wins} wins, {opponent.Stats.Losses} losses.
Your car: {context.OpponentCar?.Brand} {context.OpponentCar?.Name}.
Your personality: {personality}

RULES:
- Speak in first person as this character
- Use 1960s street racer slang when appropriate
- Keep responses to 1-2 sentences maximum
- Never break character
- Never use modern references
- Be confrontational but not vulgar";
    }

    private string BuildUserPrompt(TalkContext context)
    {
        var repDiff = context.Opponent.Stats.Reputation - context.Player.Stats.Reputation;
        var playerCar = context.PlayerCar != null
            ? $"{context.PlayerCar.Brand} {context.PlayerCar.Name}"
            : "unknown car";

        return context.Trigger switch
        {
            TalkTrigger.OpponentSelected =>
                $"A racer with reputation {context.Player.Stats.Reputation} driving a {playerCar} just walked up to you at the diner. Give them a greeting.",

            TalkTrigger.TrackSelected =>
                $"The racer just selected a track for a potential race. Comment on racing them.",

            TalkTrigger.BetTypeChanged when context.IsPinkSlipBet =>
                $"The racer wants to race for pink slips! React to this high-stakes proposal.",

            TalkTrigger.ChallengeAccepted =>
                $"You just accepted their challenge. Give a confident send-off.",

            TalkTrigger.ChallengeRejected =>
                $"You're declining their challenge. Explain why (maybe they're too weak, or you don't like the terms).",

            _ => "Say something to the racer."
        };
    }

    private string GetPersonalityDescription(int aggression)
    {
        return aggression switch
        {
            >= 80 => "Hot-headed, aggressive, easily provoked, talks trash constantly",
            >= 60 => "Confident, competitive, likes to show off",
            >= 40 => "Cool and collected, respects skill, doesn't waste words",
            >= 20 => "Laid-back, friendly but competitive, good sport",
            _ => "Quiet, mysterious, lets racing do the talking"
        };
    }

    private string CleanDialogue(string raw)
    {
        // Remove quotes if the model wrapped the response
        var cleaned = raw.Trim().Trim('"', '"', '"');

        // Remove any "Character:" prefix
        var colonIndex = cleaned.IndexOf(':');
        if (colonIndex > 0 && colonIndex < 20)
            cleaned = cleaned[(colonIndex + 1)..].Trim();

        // Limit length
        if (cleaned.Length > 200)
            cleaned = cleaned[..200] + "...";

        return cleaned;
    }
}
```

### 2. Mechanic Advice (Car Improvement)

Provides contextual recommendations for car upgrades.

```csharp
// Services/Mechanic/AiMechanicService.cs

public class AiMechanicService : IMechanicService
{
    private readonly IAiService _aiService;

    public async Task<MechanicAdvice> GetAdviceAsync(MechanicContext context)
    {
        var systemPrompt = @"You are a skilled auto mechanic in 1960s Los Angeles.
You specialize in muscle cars and street racing modifications.
You give practical, era-appropriate advice.

RULES:
- Only suggest modifications available in the 1960s
- Consider the car's current state and the player's goals
- Be specific about what parts or work is needed
- Keep advice to 2-3 sentences
- Use appropriate mechanic terminology";

        var userPrompt = BuildMechanicPrompt(context);

        var response = await _aiService.GenerateAsync(new AiRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            MaxTokens = 200,
            Temperature = 0.6f,
            Category = AiRequestCategory.MechanicAdvice
        });

        if (response.Success)
        {
            return new MechanicAdvice
            {
                Message = response.Content,
                IsAiGenerated = true
            };
        }

        return GetFallbackAdvice(context);
    }

    private string BuildMechanicPrompt(MechanicContext context)
    {
        var car = context.Car;
        var goal = context.Goal;

        return $@"The player has a {car.Year} {car.Brand} {car.Name}.
Current specs: {car.Specs.Power}HP, {car.Specs.Weight}lbs, {car.Specs.Drivetrain}.
Condition: {car.Condition}%

Player's goal: {goal switch
{
    MechanicGoal.DragRacing => "Win drag races (needs fast acceleration)",
    MechanicGoal.RoadRacing => "Win road races (needs handling and top speed)",
    MechanicGoal.General => "General improvement",
    MechanicGoal.Reliability => "More reliable for daily driving",
    _ => "General improvement"
}}

Available budget: ${context.Budget:N0}

What upgrades do you recommend?";
    }
}

public enum MechanicGoal
{
    DragRacing,
    RoadRacing,
    General,
    Reliability
}

public class MechanicContext
{
    public Car Car { get; set; }
    public CarDefinition CarDefinition { get; set; }
    public MechanicGoal Goal { get; set; }
    public decimal Budget { get; set; }
    public List<string> InstalledUpgrades { get; set; }
}

public class MechanicAdvice
{
    public string Message { get; set; }
    public List<SuggestedUpgrade> Suggestions { get; set; }
    public bool IsAiGenerated { get; set; }
}
```

### 3. Opponent Generation

Creates unique opponent backstories and personalities.

```csharp
// Services/Opponents/AiOpponentGenerationService.cs

public class AiOpponentGenerationService
{
    private readonly IAiService _aiService;
    private readonly IOpponentGenerationService _baseService;

    public async Task<Opponent> GenerateOpponentAsync(OpponentGenerationParams params)
    {
        // First, generate base opponent with stats
        var opponent = _baseService.GenerateOpponent(params);

        // Then enhance with AI-generated personality
        if (await _aiService.IsAvailableAsync())
        {
            var personality = await GeneratePersonalityAsync(opponent);
            opponent.Backstory = personality.Backstory;
            opponent.Catchphrase = personality.Catchphrase;
            opponent.RacingStyle = personality.RacingStyle;
        }

        return opponent;
    }

    private async Task<OpponentPersonality> GeneratePersonalityAsync(Opponent opponent)
    {
        var systemPrompt = @"You are creating a character for a 1960s street racing game.
Generate a brief backstory and personality for a street racer.

OUTPUT FORMAT (JSON):
{
    ""backstory"": ""2-3 sentences about their history"",
    ""catchphrase"": ""A short memorable phrase they say"",
    ""racingStyle"": ""One word: aggressive/calculated/reckless/smooth/defensive""
}";

        var userPrompt = $@"Create a personality for this racer:
Name: {opponent.Name}
Nickname: ""{opponent.Nickname}""
Reputation: {opponent.Stats.Reputation}
Skill level: {opponent.Skill}/100
Aggression: {opponent.Aggression}/100
Car: {opponent.Cars.FirstOrDefault()?.DefinitionId ?? "Unknown"}

Make the backstory fit their stats (high aggression = troubled past, high skill = experienced, etc.)";

        var response = await _aiService.GenerateAsync(new AiRequest
        {
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            MaxTokens = 250,
            Temperature = 0.9f,
            Category = AiRequestCategory.OpponentCreation
        });

        if (response.Success)
        {
            try
            {
                return JsonSerializer.Deserialize<OpponentPersonality>(response.Content);
            }
            catch
            {
                // Parse failed, return defaults
            }
        }

        return new OpponentPersonality
        {
            Backstory = null,
            Catchphrase = null,
            RacingStyle = "unknown"
        };
    }
}

public class OpponentPersonality
{
    public string Backstory { get; set; }
    public string Catchphrase { get; set; }
    public string RacingStyle { get; set; }
}
```

### 4. Future: Race Commentary

Post-race AI-generated summary of what happened.

```csharp
public class RaceCommentaryService
{
    public async Task<string> GenerateCommentaryAsync(RaceResult result)
    {
        var systemPrompt = @"You are a 1960s radio announcer covering illegal street races.
Write a brief, exciting recap of the race that just happened.
Use dramatic language appropriate to the era.
Keep it to 3-4 sentences.";

        var userPrompt = $@"Race just finished:
Winner: {result.WinnerName}
Loser: {result.LoserName}
Race type: {result.RaceType}
Track: {result.TrackName}
Winning margin: {result.WinningMargin}
Any crashes: {result.HadCrashes}
Bet type: {(result.WasPinkSlip ? "Pink slips!" : $"${result.CashWager}")}

Write the race recap.";

        // Generate and return
    }
}
```

### 5. Future: Newspaper Articles

Generate dynamic news articles about player achievements.

```csharp
public class NewspaperArticleService
{
    public async Task<NewsArticle> GenerateArticleAsync(NewsEvent newsEvent)
    {
        // Generate headline and body for events like:
        // - Player wins 10th race
        // - Player beats the #1 ranked racer
        // - Player wins a pink slip race
        // - New racer appears at the diner
    }
}
```

---

## Configuration & Settings

### Settings Model

```csharp
// Models/Settings/AiSettings.cs

public class AiSettings
{
    /// <summary>
    /// Whether AI features are enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Service type: "ollama", "lmstudio", "custom"
    /// </summary>
    public string ServiceType { get; set; } = "ollama";

    /// <summary>
    /// Service endpoint URL
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Model identifier
    /// </summary>
    public string ModelId { get; set; } = "llama3.2:3b";

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Which features use AI (allows granular control)
    /// </summary>
    public AiFeatureFlags Features { get; set; } = new();
}

public class AiFeatureFlags
{
    public bool OpponentDialogue { get; set; } = true;
    public bool MechanicAdvice { get; set; } = true;
    public bool OpponentGeneration { get; set; } = true;
    public bool RaceCommentary { get; set; } = false;  // Experimental
    public bool NewspaperArticles { get; set; } = false;  // Experimental
}
```

### Settings UI

Add to Settings screen:

```xml
<!-- AI Integration Section -->
<Border Background="#1A1A1A" CornerRadius="5" Padding="15" Margin="0,0,0,20">
    <StackPanel>
        <TextBlock Text="AI INTEGRATION (OPTIONAL)"
                   FontWeight="Bold"
                   FontSize="16"
                   Margin="0,0,0,15"/>

        <TextBlock Text="Connect to a local LLM for dynamic content generation."
                   Foreground="{StaticResource TextSecondaryBrush}"
                   TextWrapping="Wrap"
                   Margin="0,0,0,15"/>

        <!-- Enable Toggle -->
        <CheckBox Content="Enable AI Features"
                  IsChecked="{Binding AiSettings.Enabled}"
                  Margin="0,0,0,15"/>

        <!-- Service Selection -->
        <StackPanel Visibility="{Binding AiSettings.Enabled,
                    Converter={StaticResource BooleanToVisibilityConverter}}">

            <TextBlock Text="Service:" Margin="0,0,0,5"/>
            <ComboBox SelectedValue="{Binding AiSettings.ServiceType}"
                      Margin="0,0,0,15">
                <ComboBoxItem Content="Ollama" Tag="ollama"/>
                <ComboBoxItem Content="LM Studio" Tag="lmstudio"/>
                <ComboBoxItem Content="Custom Endpoint" Tag="custom"/>
            </ComboBox>

            <!-- Endpoint -->
            <TextBlock Text="Endpoint:" Margin="0,0,0,5"/>
            <TextBox Text="{Binding AiSettings.Endpoint}"
                     Margin="0,0,0,15"/>

            <!-- Model Selection -->
            <TextBlock Text="Model:" Margin="0,0,0,5"/>
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <ComboBox Grid.Column="0"
                          ItemsSource="{Binding AvailableModels}"
                          SelectedValue="{Binding AiSettings.ModelId}"/>
                <Button Grid.Column="1"
                        Content="Refresh"
                        Command="{Binding RefreshModelsCommand}"
                        Margin="10,0,0,0"/>
            </Grid>

            <!-- Test Connection -->
            <Button Content="Test Connection"
                    Command="{Binding TestAiConnectionCommand}"
                    Margin="0,20,0,10"
                    HorizontalAlignment="Left"/>
            <TextBlock Text="{Binding AiConnectionStatus}"
                       Foreground="{Binding AiConnectionStatusColor}"/>

            <!-- Feature Toggles -->
            <TextBlock Text="Enabled Features:"
                       FontWeight="Bold"
                       Margin="0,20,0,10"/>
            <CheckBox Content="Opponent Dialogue"
                      IsChecked="{Binding AiSettings.Features.OpponentDialogue}"/>
            <CheckBox Content="Mechanic Advice"
                      IsChecked="{Binding AiSettings.Features.MechanicAdvice}"/>
            <CheckBox Content="Opponent Backstories"
                      IsChecked="{Binding AiSettings.Features.OpponentGeneration}"/>
        </StackPanel>
    </StackPanel>
</Border>
```

---

## Service Registration

```csharp
// App.xaml.cs

public partial class App : Application
{
    public IAiService AiService { get; private set; }
    public ITalkService TalkService { get; private set; }

    private void InitializeAiServices()
    {
        var settings = LoadAiSettings();

        if (settings.Enabled)
        {
            AiService = settings.ServiceType switch
            {
                "ollama" => new OllamaService(new AiServiceConfig
                {
                    ServiceType = "ollama",
                    Endpoint = settings.Endpoint,
                    ModelId = settings.ModelId,
                    TimeoutSeconds = settings.TimeoutSeconds
                }, Logger),

                "lmstudio" => new LmStudioService(new AiServiceConfig
                {
                    ServiceType = "lmstudio",
                    Endpoint = settings.Endpoint,
                    ModelId = settings.ModelId,
                    TimeoutSeconds = settings.TimeoutSeconds
                }, Logger),

                _ => new NullAiService()
            };
        }
        else
        {
            AiService = new NullAiService();
        }

        // Talk service with AI support
        var staticTalkService = new StaticTalkService();
        TalkService = settings.Enabled && settings.Features.OpponentDialogue
            ? new AiTalkService(AiService, staticTalkService)
            : staticTalkService;
    }
}
```

---

## Recommended Models

| Model | Size | Speed | Quality | Best For |
|-------|------|-------|---------|----------|
| llama3.2:1b | 1.3GB | Very Fast | Good | Quick dialogue |
| llama3.2:3b | 2.0GB | Fast | Better | General use (recommended) |
| mistral:7b | 4.1GB | Medium | Great | Complex generation |
| phi3:mini | 2.2GB | Fast | Good | Low VRAM systems |

**Recommended default:** `llama3.2:3b` - good balance of speed and quality for dialogue generation.

---

## Error Handling & Fallbacks

Every AI-powered service MUST have a fallback:

```csharp
public async Task<string> GetSomethingAsync(Context context)
{
    // Try AI first
    if (await _aiService.IsAvailableAsync())
    {
        var response = await _aiService.GenerateAsync(request);
        if (response.Success)
            return response.Content;
    }

    // Always have a fallback
    return GetFallbackContent(context);
}
```

**Fallback strategies:**
- **Dialogue:** Pre-written phrases categorized by context
- **Mechanic advice:** Fixed upgrade recommendation logic
- **Opponent generation:** Random stat-based generation (current system)
- **Commentary:** Skip entirely or use templates

---

## Performance Considerations

1. **Async everywhere** - Never block UI on AI requests
2. **Timeout handling** - 30 second default, configurable
3. **Caching** - Consider caching generated content for same context
4. **Request queuing** - Don't spam the LLM with rapid requests
5. **Graceful degradation** - UI should never wait indefinitely

```csharp
// Example: Non-blocking dialogue loading
private async void OnOpponentSelected(OpponentDisplayViewModel opponent)
{
    SelectedOpponent = opponent;
    OpponentMessage = "...";  // Show loading indicator

    // Fire and forget with timeout
    _ = LoadOpponentMessageAsync(opponent);
}

private async Task LoadOpponentMessageAsync(OpponentDisplayViewModel opponent)
{
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var message = await _talkService.GetMessageAsync(context);
        OpponentMessage = message;
    }
    catch (OperationCanceledException)
    {
        OpponentMessage = GetQuickFallback(opponent);
    }
}
```

---

## Testing

### Manual Testing Checklist
- [ ] AI disabled: All features work with static fallbacks
- [ ] Ollama unavailable: Graceful fallback, no crashes
- [ ] Slow response: UI remains responsive
- [ ] Invalid response: Fallback used
- [ ] Model switch: New model used for subsequent requests

### Test Connection Command

```csharp
public async Task<(bool Success, string Message)> TestConnectionAsync()
{
    if (!await _aiService.IsAvailableAsync())
        return (false, "Cannot reach AI service");

    var response = await _aiService.GenerateAsync(new AiRequest
    {
        SystemPrompt = "You are a test assistant.",
        UserPrompt = "Say 'Connection successful!' and nothing else.",
        MaxTokens = 20,
        Temperature = 0.0f
    });

    if (response.Success)
        return (true, $"Connected! Response: {response.Content}");

    return (false, $"Error: {response.Error}");
}
```

---

## Implementation Order

1. **Phase 1:** Core infrastructure
   - `IAiService` interface
   - `NullAiService` fallback
   - `OllamaService` implementation
   - Settings model and persistence

2. **Phase 2:** Talk Service (Diner)
   - `ITalkService` interface
   - `StaticTalkService` (fallback)
   - `AiTalkService` (AI-powered)
   - Integration with DinerScreenViewModel

3. **Phase 3:** Settings UI
   - AI settings section
   - Connection testing
   - Model selection

4. **Phase 4:** Additional features
   - Mechanic advice
   - Opponent generation enhancement
   - (Future) Race commentary
   - (Future) Newspaper articles

---

## Files to Create

```
Services/
├── AI/
│   ├── IAiService.cs
│   ├── AiRequest.cs
│   ├── AiResponse.cs
│   ├── AiServiceConfig.cs
│   ├── NullAiService.cs
│   ├── OllamaService.cs
│   └── LmStudioService.cs
├── Talk/
│   ├── ITalkService.cs
│   ├── TalkContext.cs
│   ├── StaticTalkService.cs
│   └── AiTalkService.cs
└── Mechanic/
    ├── IMechanicService.cs
    ├── MechanicContext.cs
    └── AiMechanicService.cs

Models/
└── Settings/
    ├── AiSettings.cs
    └── AiFeatureFlags.cs
```
