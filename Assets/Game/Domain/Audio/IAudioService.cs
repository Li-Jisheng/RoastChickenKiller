namespace CK.Domain.Audio
{
    /// <summary>
    /// 阶段一不包含音频资源。保留接口与 cue id，
    /// 使表现层已经能发出正确事件；后续可以接入真实服务，
    /// 而无需改动战斗代码。
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
