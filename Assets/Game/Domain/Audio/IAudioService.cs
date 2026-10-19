namespace CK.Domain.Audio
{
    /// <summary>
    /// Audio is out of scope for phase 1. The interface and cue ids exist so the
    /// presentation layer already emits the right events; a real service can be
    /// plugged in later without touching combat code.
    /// </summary>
    public interface IAudioService
    {
        int PlayedCueCount { get; }
        void PlayCue(string cueId);
        void ResetCounters();
    }

    public static class AudioCues
    {
        public const string AttackNormal = "attack.normal";
        public const string AttackHeavy = "attack.heavy";
        public const string AttackSkill = "attack.skill";
        public const string Hit = "combat.hit";
        public const string Blocked = "combat.blocked";
        public const string Dodged = "combat.dodged";
        public const string BuffApplied = "buff.applied";
        public const string BuffExpired = "buff.expired";
        public const string WeaponSwitch = "ui.weapon_switch";
        public const string Dodge = "movement.dodge";
    }
}
