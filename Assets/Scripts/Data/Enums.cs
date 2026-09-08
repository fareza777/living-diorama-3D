namespace LivingDiorama.Data
{
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
        Legendary = 4,
    }

    /// <summary>How a creature carries itself when it moves. Drives the procedural
    /// animator, which is what lets a brand new GLB look alive without any clips.</summary>
    public enum LocomotionStyle
    {
        Walk,      // bipedal stride: gentle bob + counter-sway
        Scurry,    // small, fast, twitchy steps
        Hop,       // slime / frog: big arcs with heavy squash and stretch
        Trot,      // quadruped: four-beat bob, body roll
        Stomp,     // heavy armoured: slow, weighty, lands hard
        Float,     // hovers, no ground contact, lazy drift
        Slither,   // serpentine lateral wave
    }

    public enum Diet
    {
        None,        // does not eat
        Herbivore,   // berries, mushrooms
        Carnivore,   // meat
        Omnivore,
        Mineral,     // slimes: crystals, ore
        Souls,       // undead: feeds off essence motes
    }

    /// <summary>When a creature prefers to be awake. Nocturnal creatures come out
    /// at night, which is what makes the skeleton feel different from the goblin.</summary>
    public enum ActivityCycle
    {
        Diurnal,
        Nocturnal,
        Crepuscular, // dawn and dusk
        Always,
    }

    public enum BodySize
    {
        Tiny = 0,
        Small = 1,
        Medium = 2,
        Large = 3,
        Huge = 4,
    }

    /// <summary>How one creature regards another. Resolved from tag rules at runtime.</summary>
    public enum Stance
    {
        Neutral = 0,
        Friendly = 1,   // will approach and socialise
        Curious = 2,    // will investigate briefly
        Wary = 3,       // keeps distance
        Fearful = 4,    // flees on sight
        Hostile = 5,    // attacks on sight
        Predatory = 6,  // hunts, chases to kill/scare off
    }

    public enum CurrencyKind
    {
        Coins = 0,
        Essence = 1,
        BoxKeys = 2,
    }
}
