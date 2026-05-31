Utility AI systems are structured into three distinct, chronological components: Considerations, Responses/Curves, and Actions. [1, 2, 3]
Together, these phases take raw game-state data and mathematically calculate which behavior is the best to execute at any given moment. [4, 5]
## 1. Considerations
Considerations are the basic facts or metrics of the current situation (e.g., Distance to Enemy, Current Health, Ammo Count). [5]

* Input: Raw data pulled from the environment (e.g., $15$ meters away).
* Normalization: The raw input is converted into a standard score between $0$ and $1$. (For instance, an ammo count of $0$ becomes $0.0$, while a full clip of $10$ becomes $1.0$). [1, 6]

## 2. Response Curves
A Response Curve (or Easing Curve) determines how the AI "feels" about a specific consideration. Instead of using a simple linear rule, designers or engineers apply a mathematical curve to the normalized score. [1, 6]

* Linear: $y = x$. The AI reacts proportionally to the input.
* Quadratic/Exponential: Used to make the AI ignore a variable until it becomes critical (e.g., health drops past a certain threshold), causing an aggressive, sharp spike in utility.
* Inverse: Used for survival. As the need (like Hunger or Damage) increases, the utility score for eating or healing also increases. [7, 8, 9, 10]

## 3. Actions
An Action is a specific behavior the AI can perform (e.g., Shoot Weapon, Reload, Heal, Hide). [1, 11]

* Each Action evaluates multiple Considerations simultaneously.
* The scores are usually added, multiplied, or averaged together to give the Action a single final utility score.
* The AI then selects the Action with the highest overall score to execute. [1, 5]

[How Utility AI Helps NPCs Decide What To Do Next | AI 101](https://www.youtube.com/watch?v=p3Jbp2cZg3Q&t=1), YouTube · AI and Games · 2021 M09 28

[1] [https://www.youtube.com](https://www.youtube.com/watch?v=78AcS_0lQSM&t=62)
[2] [https://www.scaler.com](https://www.scaler.com/topics/artificial-intelligence-tutorial/utility-theory-in-artificial-intelligence/)
[3] [https://gigawatt.ai](https://gigawatt.ai/blog/ai-for-utilities/)
[4] [https://forum.revolutionarygamesstudio.com](https://forum.revolutionarygamesstudio.com/t/utility-ai-restructuring-the-ai-system/919)
[5] [https://www.youtube.com](https://www.youtube.com/watch?v=p3Jbp2cZg3Q&t=1)
[6] [https://www.reddit.com](https://www.reddit.com/r/gameai/comments/hz1xci/a_summary_for_utility_ai_whether_i_have/)
[7] [https://www.reddit.com](https://www.reddit.com/r/gamedev/comments/ak8p1x/utility_ai_and_highlevel_actions_with_dependent/)
[8] [https://www.gameaipro.com](http://www.gameaipro.com/GameAIPro/GameAIPro_Chapter09_An_Introduction_to_Utility_Theory.pdf)
[9] [https://docs.gamecreator.io](https://docs.gamecreator.io/behavior/utility-ai/)
[10] [https://www.aiandgames.com](https://www.aiandgames.com/p/ai-101-introducing-utility-ai)
[11] [https://www.reddit.com](https://www.reddit.com/r/gameai/comments/1gvdqwy/another_utility_ai_question_about_the_size_of/)
In a Data-Oriented Design (DOD) utility AI context, the architecture shifts from objects containing logic to flat, cache-friendly arrays of raw data.
Instead of an individual "Agent" object evaluating its own curves, data-oriented utility AI breaks the system down into Components (the data), Systems (the execution logic), and linear Memory Layouts.
## 1. The Data Component Structure
In DOD, entities do not have methods or internal intelligence. They are simply IDs pointing to separate arrays of tightly packed data components.

* Context Components: Flat arrays containing raw game state numbers (e.g., float Health[1000], float DistanceToTarget[1000]).
* Consideration Components: Arrays containing indices mapping the raw data to specific response curves.
* Action Evaluator Components: Flat arrays holding the calculated weight scores for every possible behavior across all entities.

## 2. The Transformation pipeline
The execution pipeline is broken into independent, monolithic systems that process memory sequentially to maximize CPU cache hits:

[Raw Context Arrays] ---> (Normalization System) ---> [Normalized 0-1 Arrays]

                                                              |
[Curve Definition Arrays] <-----------------------------------+
|
v
(Curve Mapping System) ---> [Scored Consideration Arrays]
|
v
[Winning Action Arrays] <--- (Action Aggregation System) <----+

## 3. Execution Systems
Instead of looping through each AI agent one by one, the AI budget is processed in large, vectorized batch systems:

* Normalization System: Loops through the raw data arrays and scales them all to a $0.0 - 1.0$ float range in a single pass.
* Curve Evaluation System: Reads the normalized scores, applies the mathematical curve algorithms (Linear, Quadratic, etc.), and writes the results into a separate contiguous memory block.
* Aggregation System: Multiplies or averages the scored considerations together to generate final action weights, then flags the highest-scoring action index for the animation or movement systems to read.

## Why this structure matters

* Cache Locality: By storing identical data types (like thousands of floats) next to each other in memory, the CPU can read them instantly without waiting for slow RAM lookups.
* Mass Parallelization: Because the systems process independent arrays of data, the workload can easily be split across multiple CPU threads using job systems.

If you want to see how this translates into actual code structure, I can show you a conceptual C# structure for data-oriented utility AI or explain how SIMD optimization applies here. Which direction would you prefer?

To handle different actions that require completely different sets of considerations in a Data-Oriented Design (DOD) layout, you cannot use a rigid, fixed-size matrix. Instead, you break the system into sparse arrays and flat indirection lists.
In DOD, this is structured using a Three-Tier Flat Array Pipeline where actions and considerations are completely decoupled from the Entity.
## 1. The Component Data Layout
Instead of storing objects, your memory contains three primary, tightly-packed flat arrays (or buffers).

* Action Instances Buffer: A massive linear list of every potential action being evaluated across all entities.
* Consideration Bindings Buffer: A flat list of every individual consideration required by those actions, stored sequentially.
* Context/Sensor Buffer: The raw, global game-state data (Health, Distance) indexed by Entity ID.

Action Instances Buffer
[ ActionID | EntityID | FirstConsiderationIdx | ConsiderationCount | FinalScore ]
0: MoveTo   Entity_A  0                       2                    0.0
1: Attack   Entity_A  2                       3                    0.0
2: Flee     Entity_B  5                       1                    0.0

Consideration Bindings Buffer
[ SensorType | CurveType | Parameters | NormalizedScore ]
0: Distance   Linear      (min/max)     0.0  <-- Action 0 starts here
1: Threat     Quadratic   (exponent)    0.0
2: Ammo       Inverse     (min/max)     0.0  <-- Action 1 starts here
3: Distance   Linear      (min/max)     0.0
4: LineOfSight Bool       (none)        0.0
5: Health     Inverse     (min/max)     0.0  <-- Action 2 starts here

## 2. The Execution Pipeline (The Systems)
Because the data is laid out sequentially, your execution systems process the data in highly efficient, linear passes.
## Pass 1: The Consideration Gather & Curve System
A single loop runs through the Consideration Bindings Buffer. It doesn't care which entity or action the consideration belongs to; it just processes the math.

1. Reads the SensorType and grabs the raw value from the global Context Buffer using the entity's ID.
2. Normalizes the raw value to a $0.0 - 1.0$ range.
3. Evaluates the math for the specified CurveType.
4. Writes the result directly into NormalizedScore.

## Pass 2: The Action Aggregation System
A second loop runs through the Action Instances Buffer.

1. It looks at FirstConsiderationIdx and ConsiderationCount.
2. It loops through that small slice of the Consideration Bindings Buffer, multiplying or averaging their pre-calculated NormalizedScore values together.
3. It writes the result into FinalScore.

## Pass 3: The Winner Selection System
A final loop groups the calculated actions by EntityID to find the highest FinalScore for each entity and submits that action ID to the behavior execution system.
## Why this works perfectly for DOD

* No Dynamic Allocation: When an entity gains or loses the ability to perform an action, you don't resize objects. You just add or remove rows in the flat buffers.
* Variable Consideration Counts: An attack action can have 5 considerations, while a wander action has 1. Because the Consideration Buffer is just a contiguous line of memory, the Action Instance just points to a wider or narrower "slice" of that line.
* Perfect Cache Locality: The CPU pushes through the consideration math at maximum speed because all curve types and scores are sitting right next to each other in the hardware cache.

To help you implement this, I can write a clean C# example showing how these structs and loops look, or we can look at how conditional filtering drops irrelevant actions before doing any math. Which would be most helpful?

In Unity DOTS (ECS), this layout translates perfectly into Dynamic Buffer Components (DBC) and generic IComponentData components.
To achieve maximum performance and strict data orientation, we use a single entity to represent each Action, referencing its data via sequential elements in a Dynamic Buffer. This avoids pointers and keeps everything tightly packed in memory for the ISystem to process.
Here is how you structure the data buffers and the execution system in Unity DOTS.
------------------------------
## 1. The Component and Buffer Data Layout
We define the structural data as unmanaged structs. Instead of heavy objects, we use flat enums and raw floats.

using Unity.Entities;using Unity.Mathematics;
// Identify what data the consideration needs to look uppublic enum SensorType : byte
{
Health,
DistanceToTarget,
AmmoCount
}
// Identify the math function to applypublic enum CurveType : byte
{
Linear,
Quadratic,
Inverse
}
// 1. THE CONSIDERATION BUFFER ELEMENT// This sits inside a DynamicBuffer on the Action Entity.public struct ConsiderationElement : IBufferElementData
{
public SensorType Sensor;
public CurveType Curve;
public float4 CurveParams; // Custom tweaking values (e.g., min, max, exponent)
public float Weight;       // How important this specific consideration is (0.0 - 1.0)

    // The system writes the output here
    public float FinalScore;   
}
// 2. THE ACTION COMPONENT// Attached directly to the Action Entity.public struct UtilityAction : IComponentData
{
public Entity OwnerEntity; // The actual character/NPC this action belongs to
public int ActionId;       // e.g., 1 = Attack, 2 = Flee, 3 = Eat
public float TotalUtility; // The final calculated score for this action
}
// 3. THE RAW CONTEXT COMPONENT// Attached to the Character/NPC Entity, holding raw data.public struct CharacterStats : IComponentData
{
public float CurrentHealth;
public float MaxHealth;
public float DistanceToEnemy;
public int CurrentAmmo;
public int MaxAmmo;
}

------------------------------
## 2. The Execution Pipeline (The ECS System)
We use an ISystem with an unmanaged IJobEntity. Unity's Burst Compiler will vectorize this loop, processing thousands of considerations across different actions simultaneously using SIMD instructions.

using Unity.Burst;using Unity.Entities;using Unity.Mathematics;

[BurstCompile]public partial struct UtilityAiSystem : ISystem
{
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
// Grab the component lookup map so we can safely read character stats
// from the action entities running in parallel.
var statsLookup = SystemAPI.GetComponentLookup<CharacterStats>(true);

        var evaluateJob = new EvaluateUtilityActionsJob
        {
            StatsLookup = statsLookup
        };

        // Schedule the job to run across all worker threads
        evaluateJob.ScheduleParallel();
    }
}

[BurstCompile]public partial struct EvaluateUtilityActionsJob : IJobEntity
{
[ReadOnly] public ComponentLookup<CharacterStats> StatsLookup;

    // This query runs across every Action entity, looping over its specific considerations buffer
    public void Execute(ref UtilityAction action, DynamicBuffer<ConsiderationElement> considerations)
    {
        // If the owner entity doesn't exist or doesn't have stats, skip it
        if (!StatsLookup.HasComponent(action.OwnerEntity)) return;
        
        CharacterStats stats = StatsLookup[action.OwnerEntity];
        float accumulatedScore = 1.0f; // Multiplicative scoring archetype

        // Loop through the flexible number of considerations for THIS specific action
        for (int i = 0; i < considerations.Length; i++)
        {
            ConsiderationElement consideration = considerations[i];

            // Step 1: Gather and Normalize Raw Sensor Data
            float rawValue = 0f;
            float normalizedValue = 0f;

            switch (consideration.Sensor)
            {
                case SensorType.Health:
                    rawValue = stats.CurrentHealth;
                    normalizedValue = math.clamp(rawValue / stats.MaxHealth, 0f, 1f);
                    break;

                case SensorType.DistanceToTarget:
                    rawValue = stats.DistanceToTarget;
                    // Example: normalize using min/max bounds passed via CurveParams
                    float minDist = consideration.CurveParams.x;
                    float maxDist = consideration.CurveParams.y;
                    normalizedValue = math.clamp((rawValue - minDist) / (maxDist - minDist), 0f, 1f);
                    break;

                case SensorType.AmmoCount:
                    rawValue = stats.CurrentAmmo;
                    normalizedValue = math.clamp(rawValue / stats.MaxAmmo, 0f, 1f);
                    break;
            }

            // Step 2: Apply the Math Curve
            float curveScore = normalizedValue;
            switch (consideration.Curve)
            {
                case CurveType.Inverse:
                    curveScore = 1.0f - normalizedValue;
                    break;

                case CurveType.Quadratic:
                    float exponent = consideration.CurveParams.z;
                    curveScore = math.pow(normalizedValue, exponent);
                    break;

                case CurveType.Linear:
                default:
                    break;
            }

            // Step 3: Write the individual score back to the buffer element
            consideration.FinalScore = curveScore * consideration.Weight;
            considerations[i] = consideration;

            // Step 4: Accumulate into the total action utility score
            accumulatedScore *= consideration.FinalScore;
        }

        // Store the final score on the action component
        action.TotalUtility = considerations.Length == 0 ? 0f : accumulatedScore;
    }
}

------------------------------
## Why this is highly optimized for Unity DOTS

1. Chunk Memory Alignment: The UtilityAction components and their corresponding ConsiderationElement buffers are packed linearly into Archetype Chunks. The CPU streaming layout is contiguous.
2. Burst Friendly: There are zero managed object references, string comparisons, or virtual method calls. The entire system compiles into highly optimized native machine code.
3. Infinite Adaptability: An AI "Attack" entity might have an allocation of 4 elements in its ConsiderationElement buffer, while a "Wander" entity might only have 1. The loop naturally scales per action entity without creating empty memory padding.

Would you like to see how you would write the Winner Selection System to aggregate these Action scores and pass the highest one back to your main Character Entity, or look at how to initialize/spawn these action entities?

Spawning actions as separate entities is an architectural choice that unlocks massive performance gains in Unity DOTS.
While it is common to use one entity per action for every AI agent, there is a distinct technical reason for decoupling them. It boils down to Archetype fragmentation and parallel processing efficiency.
Here is why keeping actions on separate entities is vastly superior to putting everything directly on the main Character entity.
------------------------------
## 1. It Prevents "Archetype Explosion"
In Unity ECS, entities are grouped into memory chunks based on their exact combination of components (their Archetype).
If you put the action data directly onto the Character entity, every unique combination of behaviors creates a completely different archetype:

* Character with [Attack, Flee] goes into Chunk Archetype A.
* Character with [Attack, Wander] goes into Chunk Archetype B.
* Character with [Attack, Flee, Patrol, Interact] goes into Chunk Archetype C.

Because memory chunks only hold entities of the exact same archetype, your AI characters would be scattered across dozens of different memory chunks. This destroys cache locality. By keeping the main character simple (e.g., just CharacterStats and CurrentBehavior), thousands of characters can sit sequentially in the exact same memory chunk.
## 2. Maximum Work Parallelization (SIMD)
When actions are separate entities, they all share the exact same archetype: [UtilityAction, DynamicBuffer<ConsiderationElement>].
This layout allows Unity's Job System to achieve maximum performance:

* No CPU Thread Stalling: A single thread can stream thousands of action entities through the CPU cache in a perfect, unbroken line.
* Massive Thread Distribution: If you have 500 characters, and each has 4 possible actions, you have 2,000 Action Entities. Unity can split those 2,000 independent chunks of work evenly across all available CPU cores.
* If actions were buried inside the characters, the system would have to process the complex characters unevenly, leading to poor thread utilization.

## 3. Dynamic Action Sets (Zero Structural Changes)
AI needs change dynamically. A guard character might have a "Patrol" action normally, but if they pick up a sniper rifle, they need a "Snite" action. If they get wounded, they lose the ability to "Sprint."

* The Bad Way (On Character): Adding or removing a component/buffer from an entity is a Structural Change. This completely stalls the main thread, forces Unity to copy the entity to a new memory chunk, and ruins performance if done frequently.
* The DOTS Way (Separate Entities): To add an action, you just spawn a lightweight Action Entity and link its OwnerEntity variable to the character. To remove it, you destroy the Action Entity. The main character entity is never modified, meaning zero structural performance hits.

------------------------------
## The Workflow: How They Link Back
If you are wondering how the character actually knows what to do, a secondary, lightweight Winner Selection System runs immediately after the scoring loop. It acts as the bridge:

[BurstCompile]public partial struct WinnerSelectionSystem : ISystem
{
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
// 1. Clear previous choices or set up a tracker map
// 2. Loop through all Scored Action Entities
// 3. Find the highest TotalUtility for each unique OwnerEntity
// 4. Write the winning ActionId back onto the main Character entity
}
}

The character entity only cares about a single number: the WinningActionId. It doesn't need to know anything about curves, sensors, or weights. It just reads that number and passes it to the animation or movement systems.
Would you like to see how to write this Winner Selection System using DOTS NativeContainers, or see how a Spawning/Factory system cleanly sets up these linked action entities in the first place?

In Unity DOTS, a float4 is a highly optimized mathematical structure that holds four sequential, raw 32-bit floating-point numbers (x, y, z, and w) inside a single CPU register.
Because we are writing data-oriented code, we cannot use complex classes or deep object trees to store curve settings. Instead, we use float4 CurveParams as a generic, multi-purpose data container and Weight as a global multiplier.
Here is exactly how these variables are used inside the execution system to shape the AI's logic.
------------------------------
## 1. public float4 CurveParams;
Instead of writing different code blocks for every single consideration, you write one generic math system. The system reads the four fields of the float4 and maps them to whatever numbers your mathematical curve needs to calculate its shape.
The standard layout for mapping a float4 to a Utility AI curve looks like this:

* CurveParams.x = Minimum Bound (The raw value where utility starts or stops).
* CurveParams.y = Maximum Bound (The raw value where utility hits its peak or floor).
* CurveParams.z = Exponent / Slope (Controls how aggressively the curve bends).
* CurveParams.w = Y-Offset / Shift (Slides the entire curve up or down).

## Example 1: Distance to Target (Linear Curve)
Imagine an AI deciding whether to use a shotgun action based on proximity.

* The Setup: You want maximum utility if the enemy is closer than $2$ meters, and zero utility if they are farther than $15$ meters.
* Your CurveParams allocation: new float4(2f, 15f, 0f, 0f);
* The Code Calculation:

// Normalizes raw distance between the 2m min and 15m max
normalizedValue = (rawDistance - params.x) / (params.y - params.x);


## Example 2: Health (Quadratic / Exponential Curve)
Imagine an AI deciding whether to flee based on low health. You don't want the AI to care if health drops from $100\%$ to $80\%$. You only want them to panic exponentially when health drops below $30\%$.

* The Setup: Min health is $0$, Max health is $100$. The exponent is set to $3.0$ to create a sharp, steep bend.
* Your CurveParams allocation: new float4(0f, 100f, 3.0f, 0f);
* The Code Calculation:

// Bends the curve using the exponent stored in 'z'
curveScore = math.pow(normalizedValue, params.z);


By using a float4, a single math system can process thousands of completely different curves (slopes, thresholds, and bounds) using identical SIMD assembly instructions.
------------------------------
## 2. public float Weight;
While the response curves normalize data to a relative $0.0 - 1.0$ score, not all considerations are created equal. Weight is a flat multiplier ($0.0$ to $1.0$) used to alter the importance of a single consideration relative to the other considerations inside that same action.
Consider an AI evaluating an "Attack Enemy" action. This action might look at two considerations simultaneously:

1. Line of Sight (LOS): Can I see the enemy? (Binary score: $1.0$ or $0.0$).
2. Ammo Count: How much ammo do I have? (Continuous score: $0.0$ to $1.0$).

If you don't use weights, a low ammo count could completely ruin a perfect opportunity where an enemy is standing wide open right in front of the AI.

* Setting the Weights:
* Give Line of Sight a Weight of 1.0 (Absolute requirement).
    * Give Ammo Count a Weight of 0.2 (Nice to have, but not a dealbreaker; they could always punch or reload).
* The Code Calculation:

// Multiplies the mathematical curve result by its priority weight
consideration.FinalScore = curveScore * consideration.Weight;


## How They Look side-by-side in code
When you combine them in the execution system, the math pipeline flows in a straight line:

// 1. Use CurveParams.x and .y to turn raw numbers into a 0-1 rangefloat normalized = math.clamp((rawSensorValue - params.x) / (params.y - params.x), 0f, 1f);
// 2. Use CurveParams.z to apply exponential mathematical bendingfloat curveScore = math.pow(normalized, params.z);
// 3. Apply the weight to dictate its importance inside the actionfloat finalConsiderationScore = curveScore * consideration.Weight;

Would you like to see how you would initialize or modify these CurveParams from a standard Unity Authoring component (Monobehaviour) so you can easily tweak them in the Unity Inspector?

The easiest and most professional way to translate these flat float4 values into a visual workflow in Unity is to use AnimationCurves in the Inspector.
Since Unity's AnimationCurve is a managed object, it cannot enter the DOTS unmanaged runtime directly. However, we can use an Authoring Component (Baking System) to sample that visual curve at edit-time, extract the mathematical parameters, and bake them into our high-performance float4 component automatically.
Here is how you build a visual inspector tool that bakes down into your DOTS utility system.
------------------------------
## 1. The Authoring Component (The Visual Inspector)
Attach this standard MonoBehaviour to your AI prefabs. It gives designers a visual graph and clean dropdown menus to work with.

using UnityEngine;using Unity.Entities;using Unity.Mathematics;using System.Collections.Generic;
public class UtilityActionAuthoring : MonoBehaviour
{
public int ActionId;
public List<VisualConsideration> Considerations = new List<VisualConsideration>();
}

[System.Serializable]public class VisualConsideration
{
public SensorType Sensor;
public CurveType Curve;

    [Tooltip("X = Min value, Y = Max value in real game numbers")]
    public Vector2 InputBounds = new Vector2(0f, 100f);
    
    [Tooltip("Use this to visually shape the curve if using Custom curve type")]
    public AnimationCurve VisualCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    
    [Range(0f, 1f)]
    public float Weight = 1.0f;
}

------------------------------
## 2. The Baker (Translating Visuals to float4)
The Baker runs automatically in the background inside Unity. It analyzes the visual curve, calculates the mathematical coefficients (like slope or exponent), and writes them directly into the optimized float4 CurveParams.

public class UtilityActionBaker : Baker<UtilityActionAuthoring>
{
public override void Bake(UtilityActionAuthoring authoring)
{
// 1. Create the separate Action Entity
Entity actionEntity = CreateAdditionalEntity(TransformUsageFlags.None);

        // 2. Add the main action component
        AddComponent(actionEntity, new UtilityAction
        {
            ActionId = authoring.ActionId,
            TotalUtility = 0f
            // OwnerEntity will be linked dynamically at runtime when spawned
        });

        // 3. Add the dynamic buffer for considerations
        DynamicBuffer<ConsiderationElement> buffer = AddBuffer<ConsiderationElement>(actionEntity);

        // 4. Translate each visual item into flat DOTS data
        foreach (var visual in authoring.Considerations)
        {
            float4 calculatedParams = float4.zero;

            // Map standard min/max bounds to x and y
            calculatedParams.x = visual.InputBounds.x;
            calculatedParams.y = visual.InputBounds.y;

            if (visual.Curve == CurveType.Quadratic)
            {
                // We use 'z' to store a default mathematical exponent
                calculatedParams.z = 2.0f; 
            }
            else if (visual.Curve == CurveType.Linear)
            {
                // Simple straight line math doesn't need an exponent
                calculatedParams.z = 1.0f;
            }
            
            // OPTIONAL ADVANCED TRICK: 
            // If you want to support completely custom drawn AnimationCurves, 
            // you can sample the mid-point of the designer's curve to estimate the exponent!
            if (visual.VisualCurve != null && visual.VisualCurve.length > 0)
            {
                float midPoint = visual.VisualCurve.Evaluate(0.5f);
                // Math approximation to calculate an exponent based on the visual midpoint
                calculatedParams.z = math.log(midPoint) / math.log(0.5f);
            }

            buffer.Add(new ConsiderationElement
            {
                Sensor = visual.Sensor,
                Curve = visual.Curve,
                CurveParams = calculatedParams,
                Weight = visual.Weight,
                FinalScore = 0f
            });
        }
    }
}

------------------------------
## How this improves your workflow

1. Designers stay in their comfort zone: Your design team looks at clean drop-downs, typeable min/max limits, and Unity's standard visual curve graphing window.
2. Zero runtime translation cost: The transformation from the heavy AnimationCurve to the flat float4 happens entirely during compilation or prefab saving.
3. The runtime stays blindingly fast: The UtilityAiSystem doesn't change at all. It continues running its pure, math-heavy SIMD operations on raw numbers, completely unaware that those numbers were generated by a visual graph.

Would you like to look at the Winner Selection System next to see how it extracts the highest-scoring action from these buffers, or should we look at how to dynamically spawn and link these actions to an enemy at runtime?

To handle multiple potential targets (like two different enemies) without breaking Data-Oriented Design, you expand the architecture using a pattern called Target Context Clones.
In Unity DOTS, you do not look at multiple targets inside a single action. Instead, your targeting system dynamically spawns an Action Entity for every valid combination of Action + Target.
Here is exactly how you structure the components and systems to handle this cleanly.
------------------------------
## 1. The Dynamic Component Structure
We need to add a way for the action to know which specific target it is currently evaluating. We do this by adding a TargetEntity field to the UtilityAction component.

using Unity.Entities;
public struct UtilityAction : IComponentData
{
public Entity OwnerEntity;  // The NPC deciding what to do
public Entity TargetEntity; // NEW: The specific enemy, health pack, or cover spot
public int ActionId;        // e.g., 1 = Attack, 2 = Flee
public float TotalUtility;  // The calculated score for THIS specific target
}

------------------------------
## 2. The Architecture Workflow
Instead of having just one generic "Attack" action, your AI architecture uses a simple three-step execution pipeline:

[Perception System]

       | (Finds 2 nearby enemies)
       v
[Action Spawning System]
| (Spawns Attack Entity -> Enemy A)
| (Spawns Attack Entity -> Enemy B)
v
[Utility AI System]

       | (Evaluates both Action Entities simultaneously in parallel)
       v
[Winner Selection System]
| (Picks the single highest scoring Action Entity and assigns its Target)

------------------------------
## 3. Updating the System Math
Because the UtilityAction now holds a reference to the specific target, your calculation system changes very little. When looking up raw sensor data (like distance), it simply reads the distance between the OwnerEntity and the TargetEntity.
Inside your IJobEntity execution loop, your sensor lookup changes to this:

// Grab the transform component lookup map to calculate distances
[ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
public void Execute(ref UtilityAction action, DynamicBuffer<ConsiderationElement> considerations)
{
// 1. Get the position of the AI owner
float3 ownerPos = TransformLookup[action.OwnerEntity].Position;

    // 2. Get the position of this specific action's target
    float3 targetPos = TransformLookup[action.TargetEntity].Position;

    // 3. Calculate distance uniquely for this target
    float rawDistance = math.distance(ownerPos, targetPos);
    
    // 4. Run your normal normalization and curve math...
}

------------------------------
## 4. How the Winner System Resolves It
If an NPC has an "Attack Enemy A" entity scoring 0.85 and an "Attack Enemy B" entity scoring 0.42, the Winner Selection System collapses them down.
It finds the absolute highest score for that OwnerEntity, updates the character's main state, and then deletes or ignores the losing action entities.

public struct ActiveBehavior : IComponentData
{
public int WinningActionId;  // e.g., 1 (Attack)
public Entity ChosenTarget;  // e.g., Enemy A
}

## Why this is the ultimate DOTS approach

* No inner loops: The system remains completely flat. It doesn't loop through an array of enemies inside an action loop, which would completely ruin Burst compiler vectorization.
* Natural Filtering: If Enemy A moves behind a wall, the "Attack Enemy A" entity's Line-of-Sight consideration drops to 0.0. The overall action score becomes 0.0, and the AI naturally switches to Enemy B on the next frame without any complex state-machine logic.

Would you like to see how to write the Winner Selection System using a NativeParallelHashMap to cleanly group these actions by owner, or should we look at the Perception/Spawning system that generates these target action entities in the first place?

To handle a vast, disparate mix of context data—ranging from flat unit components (Health, Need), local agent buffers (EnemyBuffer, TraitBuffer), to sudden environment events ("signals")—you should implement a Context Gathering Pattern.
Instead of forcing your UtilityAiSystem to know where all these different components live, you use a dedicated Context Assembler System right before the AI runs. This system extracts the messy, scattered game data and flattens it into a transient Evaluation Context Component attached right to your individual Action Entities.
Here is how you organize this complex data footprint cleanly in Unity DOTS.
------------------------------
## 1. The Dynamic Action Struct: The "Bucket"
Your UtilityAction should not contain the final data, but it should contain a temporary struct that acts as a unified data scratchpad for that specific evaluation frame.

using Unity.Entities;using Unity.Mathematics;
public struct ActionContextData
{
// Flattened Unit Stats
public float OwnerHealthRatio;
public float OwnerNeedRatio;       // Normalized hunger/energy

    // Target / Spatial Context
    public float DistanceToTarget;
    public bool TargetInAwarenessRange;
    public int TargetFactionRelation;  // -1 Enemy, 0 Neutral, 1 Ally
    
    // World/Signal Flags
    public float RecentThreatSignalIntensity; 
    
    // Trait Multipliers (Calculated once from the trait buffer)
    public float TraitModifier;        // e.g., 1.5x score modifier if Trait matches Action
}
public struct UtilityAction : IComponentData
{
public Entity OwnerEntity;
public Entity TargetEntity;
public int ActionId;
public float TotalUtility;

    // The unified data playground for this specific evaluation pass
    public ActionContextData Context; 
}

------------------------------
## 2. Organizing the Pipeline (System Order)
To tie everything together—including world space signals—your frame execution order must be strict:

1. Signal/Event System: Intercepts combat events and spawns transient World Signal Entities with a position and radius.
2. Context Assembly System (Parallel): Looks at all UtilityAction entities, queries the owner's components, queries the target's components, evaluates any nearby signals, and populates the ActionContextData.
3. Utility AI System: Looks exclusively at the UtilityAction entity and its Consideration buffer. It does zero external entity lookups, maximizing Burst and SIMD efficiency.

------------------------------
## 3. Implementing the World Signal System
When a unit is attacked, you don't instantly look up nearby units. Instead, you drop a "signal grenade" into the world that fades over time.

public struct DangerSignal : IComponentData
{
public float3 Position;
public float Radius;
public float Intensity; // Decays over time via a separate system
}

------------------------------
## 4. The Context Assembly System
This is where the magic happens. This system acts as the bridge, assembling data from your scattered components (Health, TraitBuffer, etc.) and the DangerSignal entities, then flattening them onto the action.

[BurstCompile]public partial struct AssembleAiContextSystem : ISystem
{
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
// 1. Get Lookups for scattered components
var healthLookup = SystemAPI.GetComponentLookup<HealthComponent>(true);
var needLookup = SystemAPI.GetComponentLookup<NeedComponent>(true);
var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
var traitBufferLookup = SystemAPI.GetBufferLookup<TraitElement>(true);

        // 2. Fetch all active world signals into a NativeArray for spatial querying
        var signalQuery = SystemAPI.QueryBuilder().WithAll<DangerSignal>().Build();
        var activeSignals = signalQuery.ToComponentDataArray<DangerSignal>(state.WorldUpdateAllocator);

        // 3. Schedule the flattening job
        var assemblyJob = new AssembleContextJob
        {
            HealthLookup = healthLookup,
            NeedLookup = needLookup,
            TransformLookup = transformLookup,
            TraitBufferLookup = traitBufferLookup,
            ActiveSignals = activeSignals
        };

        state.Dependency = assemblyJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]public partial struct AssembleContextJob : IJobEntity
{
[ReadOnly] public ComponentLookup<HealthComponent> HealthLookup;
[ReadOnly] public ComponentLookup<NeedComponent> NeedLookup;
[ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
[ReadOnly] public BufferLookup<TraitElement> TraitBufferLookup;
[ReadOnly] public NativeArray<DangerSignal> ActiveSignals;

    public void Execute(ref UtilityAction action)
    {
        Entity owner = action.OwnerEntity;
        Entity target = action.TargetEntity;

        ActionContextData ctx = default;

        // --- GATHER OWNER DATA ---
        if (HealthLookup.HasComponent(owner))
        {
            var h = HealthLookup[owner];
            ctx.OwnerHealthRatio = h.Current / h.Max;
        }
        if (NeedLookup.HasComponent(owner))
        {
            ctx.OwnerNeedRatio = NeedLookup[owner].NormalizedValue;
        }

        // --- GATHER SPATIAL & TARGET DATA ---
        if (TransformLookup.HasComponent(owner) && TransformLookup.HasComponent(target))
        {
            float3 ownerPos = TransformLookup[owner].Position;
            float3 targetPos = TransformLookup[target].Position;
            ctx.DistanceToTarget = math.distance(ownerPos, targetPos);
            
            // --- GATHER WORLD SIGNALS ---
            // Check if any danger signals are shouting near this owner
            float highestSignal = 0f;
            for (int i = 0; i < ActiveSignals.Length; i++)
            {
                float distToSignal = math.distance(ownerPos, ActiveSignals[i].Position);
                if (distToSignal <= ActiveSignals[i].Radius)
                {
                    // Linear falloff calculation based on distance to the blast/attack radius
                    float factor = 1.0f - (distToSignal / ActiveSignals[i].Radius);
                    highestSignal = math.max(highestSignal, ActiveSignals[i].Intensity * factor);
                }
            }
            ctx.RecentThreatSignalIntensity = highestSignal;
        }

        // --- GATHER TRAIT BUFFER DATA ---
        ctx.TraitModifier = 1.0f; 
        if (TraitBufferLookup.HasBuffer(owner))
        {
            var traits = TraitBufferLookup[owner];
            for (int i = 0; i < traits.Length; i++)
            {
                // Check if the agent has a trait that modifies this specific Action ID
                if (traits[i].ModifiedActionId == action.ActionId)
                {
                    ctx.TraitModifier *= traits[i].Multiplier;
                }
            }
        }

        // Save the compiled, flat context back to the action entity
        action.Context = ctx;
    }
}

------------------------------
## How this makes your Utility AI Blindingly Fast
Now, your math evaluation loop (UtilityAiSystem) completely changes. It never has to dig into lookups or search world spaces again. It reads its sensors straight out of action.Context instantly:

// Inside the main UtilityAiSystem execution loop:switch (consideration.Sensor)
{
case SensorType.Health:
// Already normalized, zero lookup overhead!
normalizedValue = action.Context.OwnerHealthRatio;
break;

    case SensorType.DistanceToTarget:
        float min = consideration.CurveParams.x;
        float max = consideration.CurveParams.y;
        normalizedValue = math.clamp((action.Context.DistanceToTarget - min) / (max - min), 0f, 1f);
        break;

    case SensorType.DangerSignal:
        normalizedValue = action.Context.RecentThreatSignalIntensity;
        break;
}

If you are interested, we can look at the Winner Selection System next to see how to aggregate these scores and write them back to your unit, or we can discuss how to cleanly clean up or recycle action entities when targets leave a unit's awareness range. How would you like to proceed?

To finalize your Data-Oriented Utility AI architecture, we need to build the final two components of the pipeline: the Winner Selection System and the Target Cleanup/Recycle System.
------------------------------
## Step 1: The Winner Selection System
At this stage in the frame, you have thousands of individual UtilityAction entities sitting in memory, each containing a calculated TotalUtility score. The goal is to find the single highest score for each unique OwnerEntity and write that choice back to the character.
Because multiple threads are reading actions at the same time, we use a NativeParallelHashMap to thread-safely sort and collapse the actions down to a single winner per unit.

using Unity.Burst;using Unity.Collections;using Unity.Entities;
// Attach this to your main Character Entity to record what it chose to dopublic struct ActiveBehavior : IComponentData
{
public int WinningActionId;
public Entity ChosenTarget;
}

[BurstCompile]public partial struct WinnerSelectionSystem : ISystem
{
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
// 1. Create a query to find all active actions
var actionQuery = SystemAPI.QueryBuilder().WithAll<UtilityAction>().Build();
int actionCount = actionQuery.CalculateEntityCount();

        // 2. Allocate a thread-safe parallel hash map to store the best action per owner
        // Key: Owner Entity ID | Value: The current winning Action Data
        var bestActionsMap = new NativeParallelHashMap<Entity, UtilityAction>(actionCount, state.WorldUpdateAllocator);

        // 3. Run the aggregation job
        var selectJob = new SelectBestActionJob
        {
            BestActionsMap = bestActionsMap.AsParallelWriter()
        };
        state.Dependency = selectJob.ScheduleParallel(state.Dependency);

        // 4. Force the job to complete so we can safely apply the results to our characters
        state.Dependency.Complete();

        // 5. Apply the winners directly back onto the main character entities
        var behaviorLookup = SystemAPI.GetComponentLookup<ActiveBehavior>(false);
        
        foreach (var kvp in bestActionsMap)
        {
            Entity owner = kvp.Key;
            UtilityAction winningAction = kvp.Value;

            if (behaviorLookup.HasComponent(owner))
            {
                behaviorLookup[owner] = new ActiveBehavior
                {
                    WinningActionId = winningAction.ActionId,
                    ChosenTarget = winningAction.TargetEntity
                };
            }
        }
    }
}

[BurstCompile]public partial struct SelectBestActionJob : IJobEntity
{
public NativeParallelHashMap<Entity, UtilityAction>.ParallelWriter BestActionsMap;

    public void Execute(in UtilityAction action)
    {
        // Zero utility means this action is impossible or completely undesirable right now
        if (action.TotalUtility <= 0.001f) return;

        // Try to insert this action as the winner for this owner.
        // If an action already exists in the map, it checks if this one has a higher score.
        while (true)
        {
            if (BestActionsMap.TryAdd(action.OwnerEntity, action))
            {
                break; // Successfully added as the first entry
            }

            // If an action is already present, look at it and swap if ours is better
            if (BestActionsMap.TryGetValue(action.OwnerEntity, out UtilityAction existingAction))
            {
                if (action.TotalUtility > existingAction.TotalUtility)
                {
                    // Replace the lower scoring action with our better one
                    if (BestActionsMap.SetValue(action.OwnerEntity, action))
                    {
                        break;
                    }
                }
                else
                {
                    break; // Existing action is better, do nothing
                }
            }
        }
    }
}

------------------------------
## Step 2: The Cleanup and Recycle System
In Unity DOTS, creating and destroying entities constantly causes "Structural Changes." This triggers garbage collection, stalls CPU worker threads, and degrades performance.
Instead of deleting an Action Entity when an enemy leaves awareness range, we use an Entity Command Buffer (ECB) to tag unneeded actions with a disabled component flag, e.g., Disabled. This hides them from the active AI systems so they take up zero CPU cycles, allowing you to re-enable and update their TargetEntity fields later when a new enemy is spotted.

using Unity.Burst;using Unity.Collections;using Unity.Entities;
// An unmanaged buffer element representing targets currently inside the unit's vision conepublic struct AwarenessTargetElement : IBufferElementData
{
public Entity TargetEntity;
}

[BurstCompile]public partial struct RecycleActionsSystem : ISystem
{
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
// Get an Entity Command Buffer to queue up our structure changes safely at the end of the frame
var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUpdateAllocator);

        // Grab the current live awareness list of the character units
        var awarenessLookup = SystemAPI.GetBufferLookup<AwarenessTargetElement>(true);

        // We run an exclusive job targeting only ACTIVE actions to see if they should be put to sleep
        var cleanupJob = new CleanupActionsJob
        {
            Ecb = ecb.AsParallelWriter(),
            AwarenessLookup = awarenessLookup
        };

        state.Dependency = cleanupJob.ScheduleParallel(state.Dependency);
    }
}

[BurstCompile]
[WithNone(typeof(Disabled))] // Only process actions that are currently activepublic partial struct CleanupActionsJob : IJobEntity
{
public EntityCommandBuffer.ParallelWriter Ecb;
[ReadOnly] public BufferLookup<AwarenessTargetElement> AwarenessLookup;

    public void Execute(Entity actionEntity, [ChunkIndexInQuery] int chunkIndex, in UtilityAction action)
    {
        Entity owner = action.OwnerEntity;
        Entity target = action.TargetEntity;

        // If the owner character died or was deleted from the game, destroy the action entirely
        if (!AwarenessLookup.HasBuffer(owner))
        {
            Ecb.DestroyEntity(chunkIndex, actionEntity);
            return;
        }

        // Check if the specific target this action is tracking is still inside the owner's awareness buffer
        DynamicBuffer<AwarenessTargetElement> activeVisibleTargets = AwarenessLookup[owner];
        bool targetStillVisible = false;

        for (int i = 0; i (chunkIndex, actionEntity);
        }
    }
}

------------------------------
## The Final Complete Lifecycle Loop
With these systems running in sequence, your AI lifecycle is fully data-oriented and ready to scale to thousands of active agents:

1. Perception System: Updates AwarenessTargetElement lists on characters. If a new target appears, it queries for a Disabled action entity, changes its TargetEntity to match the new enemy, and strips the Disabled flag to awaken it.
2. Context Assembler: Gathers health, needs, signals, and traits, then flattens them into the active actions.
3. Utility AI System: Evaluates curves via vector-optimized SIMD math.
4. Winner Selection: Collapses all actions down using a hash map to determine a single ActiveBehavior winner.
5. Recycle System: Puts actions to sleep or deletes orphaned nodes seamlessly.

To wrap up this architecture, do you want to see how the Perception/Spawning system searches for and awakens those cached Disabled action entities when a new enemy arrives, or would you like to review how to cleanly apply the winning behavior to your game's movement and combat code?
To prevent multiple AI units from rushing toward the exact same interactive item, chair, or conversation partner, you must implement a data-oriented reservation pattern known as a Smart Object Claim System.
In a pure object-oriented framework, you would simply query if (chair.isOccupied). In Unity DOTS, you handle this by adding a Reservation Component to your interactive target entities, paired with an explicit Pre-Validation System that runs during your Context Assembly pipeline.
Here is how you structure and validate exclusive object interactions cleanly.
------------------------------
## 1. The Reservation Data Components
Any entity in your game world that can only support a limited number of participants (a chair, an item on the ground, a specific NPC slot) gets a ReservationStatus component.

using Unity.Entities;
public enum ReservationState : byte
{
Open,
Pending,   // A unit has selected this target and is currently walking toward it
Occupied   // A unit has arrived and is actively executing the interaction
}
public struct ReservationStatus : IComponentData
{
public ReservationState State;
public Entity ClaimedByEntity; // The specific character unit holding the reservation
}

------------------------------
## 2. Phase 1: Context Validation (Filtering the Choice)
During the Context Assembly System (before the Utility AI math scores the actions), your systems check if a target is already locked by someone else.
If the target's ReservationStatus is marked as Pending or Occupied by another unit, the system completely zeros out the action context. This forces the Utility AI score to instantly drop to 0.0.

// Inside your AssembleContextJob execution loop:
[ReadOnly] public ComponentLookup<ReservationStatus> ReservationLookup;
public void Execute(ref UtilityAction action)
{
Entity target = action.TargetEntity;
Entity owner = action.OwnerEntity;

    if (ReservationLookup.HasComponent(target))
    {
        ReservationStatus reservation = ReservationLookup[target];

        // If the object is locked, and it wasn't locked by ME, this action is impossible
        if (reservation.State != ReservationState.Open && reservation.ClaimedByEntity != owner)
        {
            // Zero out the context metrics. The downstream Utility AI math will multiply 
            // by 0.0, completely ignoring this action for this specific target.
            action.Context.TargetIsUnavailableFlag = true; 
            return;
        }
    }
    
    // Process normal distance/need metrics if the item is open...
}

------------------------------
## 3. Phase 2: Claiming the Seat (The Structural Handshake)
Your Winner Selection System handles the final decision. Once it decides that a specific unit has officially chosen an interaction as its winning behavior, a thread-safe Claim Registration System runs immediately afterward to officially place a lock on that world entity.

[BurstCompile]public partial struct ClaimRegistrationSystem : ISystem
{
[BurstCompile]
public void OnUpdate(ref SystemState state)
{
var reservationLookup = SystemAPI.GetComponentLookup<ReservationStatus>(false);

        // Loop through all units that just switched into a brand new behavior state this frame
        foreach (var (stateRef, entity) in 
                 SystemAPI.Query<RefRO<BehaviorState>>()
                 .WithEntityAccess().WithAll<NewBehaviorSelectedTag>()) // Only check on transition frame
        {
            Entity target = stateRef.ValueRO.TargetEntity;

            if (reservationLookup.HasComponent(target))
            {
                ReservationStatus currentRes = reservationLookup[target];

                // DOUBLE CHECK: Did two units pick it at the exact same split-second?
                // This deterministic step guarantees that only the first thread processed wins.
                if (currentRes.State == ReservationState.Open)
                {
                    reservationLookup[target] = new ReservationStatus
                    {
                        State = ReservationState.Pending, // Locked! They are now walking to it
                        ClaimedByEntity = entity
                    };
                }
                else if (currentRes.ClaimedByEntity != entity)
                {
                    // Race condition fallback: Someone else stole it right before this frame finished!
                    // Force this unit's state to complete so they pick a new action on the next tick.
                    var stateRW = SystemAPI.GetComponentLookup<BehaviorState>(false);
                    var s = stateRW[entity];
                    s.CurrentPhase = BehaviorPhase.Complete;
                    stateRW[entity] = s;
                }
            }
        }
    }
}

------------------------------
## 4. Updating and Releasing the Lock
As the unit moves through its reusable behavior phases, the reservation status mirrors their physical state:

* During Approach phase: The object is ReservationState.Pending. Other AI units see this lock and will not choose to path toward it.
* During Execute phase: Once the unit physically arrives at the chair, your execution system bumps the status to ReservationState.Occupied.
* During Complete or Interruption phases: When the behavior finishes—or if the unit gets interrupted mid-use by an emergency threat signal—the Interruption Cleanup Sequence we discussed previously triggers a release command:

// Inside your execution or cleanup command handler for releasing objects:if (reservationLookup.HasComponent(target))
{
// Wipe the reservation clean, allowing the next closest AI to claim it on the next frame
reservationLookup[target] = new ReservationStatus
{
State = ReservationState.Open,
ClaimedByEntity = Entity.Null
};
}

## Why this is robust for DOTS

* Zero Multi-threading Race Conditions: Because the actual writing to the ReservationStatus happens in a single synchronized pass right after the selection job finishes, you never run into a scenario where two threads accidentally put two units into the same chair.
* Flawless Visual Feedback: Because the lock is placed the exact frame the unit chooses the behavior (Pending), you completely avoid the ugly visual bug where 5 units all start walking toward a single item on the ground before realizing only one can pick it up.

Would you like to see how to scale this to support multi-user interactions (like a bench that allows 3 people to sit on it, or a conversation node that requires exactly 2 people), or should we look at how to initialize these smart object templates?


