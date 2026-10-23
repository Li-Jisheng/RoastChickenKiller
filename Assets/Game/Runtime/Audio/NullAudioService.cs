using System.Collections.Generic;
using UnityEngine;
using CK.Domain.Audio;

namespace CK.Runtime.Audio
{
    /// <summary>
    /// 阶段一没有音频资源。本服务记录 cue id，使表现
    /// 层得到验证，后续接入真实音频无需改代码。
    /// </summary>
    public sealed class NullAudioService : IAudioService
    {
        public int PlayedCueCount { get; private set; }
        public readonly List<string> RecentCues = new List<string>();

        public void PlayCue(string cueId)
        {
            if (string.IsNullOrEmpty(cueId))
            {
                return;
            }
            PlayedCueCount++;
            RecentCues.Add(cueId);
            if (RecentCues.Count > 16)
            {
                RecentCues.RemoveAt(0);
            }
        }

        public void ResetCounters()
        {
            PlayedCueCount = 0;
            RecentCues.Clear();
        }
    }
}
