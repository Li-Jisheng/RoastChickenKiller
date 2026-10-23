using CK.Domain.Combat;
using UnityEngine;

namespace CK.Runtime.Combat
{
    /// <summary>把内部枚举转换成 HUD 使用的中文文本；内部标识仍保持英文/ABI 稳定。</summary>
    public static class CombatText
    {
        public static string Event(CombatEventType type)
        {
            switch (type)
            {
                case CombatEventType.Hit: return "命中";
                case CombatEventType.Blocked: return "格挡";
                case CombatEventType.Dodged: return "闪避";
                case CombatEventType.BuffApplied: return "增益应用";
                case CombatEventType.AbilityStarted: return "技能开始";
                case CombatEventType.AbilityEnded: return "技能结束";
                case CombatEventType.Rejected: return "拒绝";
                case CombatEventType.BuffExpired: return "增益结束";
                case CombatEventType.Contact: return "接触";
                case CombatEventType.BlockStarted: return "格挡开始";
                case CombatEventType.BlockEnded: return "格挡结束";
                case CombatEventType.DodgeStarted: return "闪避开始";
                default: return "无";
            }
        }

        public static string Reject(CombatRejectReason reason)
        {
            switch (reason)
            {
                case CombatRejectReason.Cooldown: return "冷却中";
                case CombatRejectReason.Busy: return "动作中";
                case CombatRejectReason.Blocking: return "格挡中";
                case CombatRejectReason.Expired: return "已过期";
                case CombatRejectReason.NoAbility: return "无技能";
                case CombatRejectReason.State: return "状态不允许";
                default: return "无";
            }
        }

        public static string Phase(int phase)
        {
            switch (phase)
            {
                case 0: return "起手";
                case 1: return "生效";
                case 2: return "收招";
                default: return "无";
            }
        }

        public static string Collision(CollisionFlags flags)
        {
            if (flags == CollisionFlags.None)
            {
                return "无";
            }
            var parts = new System.Collections.Generic.List<string>(2);
            if ((flags & CollisionFlags.Sides) != 0) parts.Add("侧面");
            if ((flags & CollisionFlags.Above) != 0) parts.Add("上方");
            if ((flags & CollisionFlags.Below) != 0) parts.Add("下方");
            return parts.Count > 0 ? string.Join("/", parts) : "无";
        }
    }
}
