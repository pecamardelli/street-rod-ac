/*
 * STREET ROD OPPONENT SYSTEM - USAGE EXAMPLES
 *
 * This file demonstrates how to use the opponent system components together.
 * The opponent system follows strict separation between:
 * - Street Rod opponent data (engine-agnostic, persisted)
 * - Assetto Corsa AI parameters (engine-specific, ephemeral)
 */

using Street_Rod_AC.Models.GameState;
using Street_Rod_AC.Services.Opponents;
using Street_Rod_AC.Services.Configuration.Models;

namespace Street_Rod_AC.Examples
{
    public class OpponentSystemUsageExample
    {
        // ===== EXAMPLE 1: GENERATING A NEW OPPONENT =====

        public void Example_GenerateNewOpponent()
        {
            var generationService = new OpponentGenerationService();

            // Generate a young aggressive opponent (age 22, male)
            var youngOpponent = generationService.GenerateOpponent(
                name: "Jake \"Hotshot\" Martinez",
                age: 22,
                gender: Gender.Male
            );
            // Result: High skill (80-100), high aggression (~50-90 due to age modifier)

            // Generate an experienced veteran (age 48, male)
            var veteranOpponent = generationService.GenerateOpponent(
                name: "Richard \"Old School\" Thompson",
                age: 48,
                gender: Gender.Male
            );
            // Result: High skill (80-100), lower aggression (~20-60 due to age modifier)

            // Generate with specific base skill
            var skilledOpponent = generationService.GenerateOpponent(
                name: "Sarah \"Speed Demon\" Chen",
                age: 28,
                gender: Gender.Female,
                baseSkill: 95
            );
            // Result: Skill locked at 95, moderate aggression with slight female tendency

            // At this point, opponents are engine-agnostic Street Rod data
            // They can be saved to LiteDB without any AC-specific values
        }

        // ===== EXAMPLE 2: CONVERTING OPPONENT TO AC AI PARAMETERS =====

        public void Example_ConvertToACParameters()
        {
            // Assume we have an opponent loaded from database
            var opponent = new Opponent(
                name: "Tony \"The Shark\" Rossi",
                age: 35,
                gender: Gender.Male,
                skill: 92,
                aggression: 75
            );

            // Convert to AC AI parameters (runtime only, never persisted)
            var aiParams = OpponentAIAdapter.ToAssettoCorsaAI(opponent);

            // Result:
            // aiParams.AILevel = 92
            // aiParams.AIAggression = 75
            // aiParams.DriverName = "Tony \"The Shark\" Rossi"

            // Validate opponent before racing
            var isValid = OpponentAIAdapter.ValidateOpponent(opponent);
            if (!isValid)
            {
                throw new InvalidOperationException("Opponent has invalid trait values");
            }
        }

        // ===== EXAMPLE 3: LAUNCHING A DRAG RACE WITH OPPONENT =====

        public void Example_LaunchDragRaceWithOpponent()
        {
            // Assume we have player and opponent
            var player = new Player("PlayerName");
            var opponent = new Opponent("Andrea Crotto", 32, Gender.Male, 90, 60);

            // Convert opponent traits to AC AI parameters
            var aiParams = OpponentAIAdapter.ToAssettoCorsaAI(opponent);

            // Create drag race launch intent with AI parameters
            var launchIntent = new DragRaceLaunchIntent
            {
                PlayerCarId = "cadillac_1949",
                PlayerSkin = "Burnt_Orange",
                PlayerName = player.Name,
                OpponentCarId = "pb_pontiac_gto_65",
                OpponentSkin = "00_black",
                OpponentName = opponent.Name,

                // AC AI parameters from adapter (ephemeral, runtime only)
                OpponentAILevel = aiParams.AILevel,          // 90
                OpponentAIAggression = aiParams.AIAggression, // 60

                // Race context for result correlation
                PlayerCarInstanceId = Guid.NewGuid(),
                OpponentCarInstanceId = Guid.NewGuid(),
                CashWager = 500m,
                IsPinkSlip = false
            };

            // Launch the race (AssettoCorsaLauncher will handle the rest)
            // The AI parameters are injected into race.ini at runtime
            // After the race, the original opponent data remains unchanged
        }

        // ===== EXAMPLE 4: EVOLVING OPPONENT AFTER RACE =====

        public void Example_EvolveOpponentAfterRace()
        {
            var evolutionService = new OpponentEvolutionService();

            var opponent = new Opponent("Mike \"Flash\" Johnson", 25, Gender.Male, 88, 65);

            // Scenario 1: Opponent wins dominantly
            evolutionService.ApplyWinEvolution(opponent, isDominantWin: true);
            // Result: Skill +1-2, Aggression +3-6

            // Scenario 2: Opponent loses badly
            evolutionService.ApplyLossEvolution(opponent, isBadlyBeaten: true);
            // Result: Skill +0-2, Aggression -8 to -3

            // Scenario 3: Opponent crashes
            evolutionService.ApplyCrashEvolution(opponent);
            // Result: Skill -2 to 0, Aggression -10 to -5

            // Scenario 4: Close race (regardless of outcome)
            evolutionService.ApplyCloseRaceEvolution(opponent);
            // Result: Skill +1-2 (learning from intense competition)

            // After evolution, save the updated opponent to database
            // The changed traits will affect future races
        }

        // ===== EXAMPLE 5: COMPLETE RACE WORKFLOW =====

        public void Example_CompleteRaceWorkflow()
        {
            var generationService = new OpponentGenerationService();
            var evolutionService = new OpponentEvolutionService();

            // 1. SETUP: Generate or load opponent
            var opponent = generationService.GenerateOpponent("Carlos Rivera", 30, Gender.Male, 85);

            // 2. PRE-RACE: Convert to AC parameters
            var aiParams = OpponentAIAdapter.ToAssettoCorsaAI(opponent);
            if (!OpponentAIAdapter.ValidateOpponent(opponent))
            {
                throw new InvalidOperationException("Invalid opponent");
            }

            // 3. LAUNCH: Create race intent with AC parameters
            var launchIntent = new DragRaceLaunchIntent
            {
                PlayerName = "Player",
                OpponentName = opponent.Name,
                OpponentAILevel = aiParams.AILevel,
                OpponentAIAggression = aiParams.AIAggression,
                // ... other properties
            };

            // 4. RACE: Launch Assetto Corsa (handled by launcher service)
            // var launchResult = await launcher.LaunchRaceAsync(launchIntent);

            // 5. POST-RACE: Process results and evolve opponent
            bool playerWon = true; // From race results
            bool closeRace = true; // Margin < 0.5 seconds
            bool opponentCrashed = false;

            if (opponentCrashed)
            {
                evolutionService.ApplyCrashEvolution(opponent);
            }
            else if (!playerWon) // Opponent won
            {
                bool dominantWin = !closeRace;
                evolutionService.ApplyWinEvolution(opponent, dominantWin);
            }
            else // Opponent lost
            {
                bool badlyBeaten = !closeRace;
                evolutionService.ApplyLossEvolution(opponent, badlyBeaten);
            }

            if (closeRace)
            {
                evolutionService.ApplyCloseRaceEvolution(opponent);
            }

            // 6. PERSIST: Save evolved opponent to database
            // gameStateRepository.SaveOpponent(opponent);

            // The opponent's traits have evolved based on race outcome
            // Next race will use the updated skill/aggression values
        }

        // ===== EXAMPLE 6: OPPONENT PERSISTENCE (WHAT TO SAVE) =====

        public void Example_OpponentPersistence()
        {
            // CORRECT: Save Street Rod opponent data (engine-agnostic)
            var opponent = new Opponent("Lisa Knight", 27, Gender.Female, 91, 58)
            {
                OpponentId = Guid.NewGuid(),
                PortraitPath = "/portraits/lisa_knight.png",
                Location = "Downtown",
                Biography = "A fearless street racer known for precision driving"
            };

            // Save this to LiteDB - it contains NO AC-specific values
            // Saved fields: OpponentId, Name, Age, Gender, Skill, Aggression,
            //               PortraitPath, Location, Biography, Stats

            // INCORRECT: Never save AC AI parameters
            // ❌ Don't add AILevel or AIAggression fields to Opponent class
            // ❌ Don't persist AssettoCorsaAIParameters
            // ❌ AC values are computed at runtime via OpponentAIAdapter.ToAssettoCorsaAI()
        }
    }
}
