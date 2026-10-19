using System.Collections.Generic;
using UnityEngine;
using CK.Domain.Audio;

namespace CK.Runtime.Audio
{
    /// <summary>
    /// Phase 1 has no audio assets. This service records cue ids so the presentation
    /// layer is exercised and future audio can be attached without code changes.
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
