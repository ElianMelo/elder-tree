using ElderTree.Audio;
using ElderTree.Combat;
using ElderTree.Enemies;
using ElderTree.FX;
using ElderTree.Player;
using UnityEngine;

namespace ElderTree.Net
{
    /// <summary>What the game asks of co-op (<see cref="ICoopBridge"/>): effects, hits and the stage flow.</summary>
    public partial class CoopSession : ICoopBridge
    {
        public void Fx(FXType type, Vector3 pos, Quaternion rot, float scale) => NetStage.Instance?.SendFx(type, pos, rot, scale);
        public void Sfx(Sfx id, Vector3 pos, float volume, float pitch) => NetStage.Instance?.SendSfx(id, pos, volume, pitch);
        public void Blast(Vector3 center, float radius, float fxScale) => NetStage.Instance?.SendBlast(center, radius, fxScale);
        public bool BossHit(BossBase boss, Component part, in DamageInfo info) => NetStage.Instance != null && NetStage.Instance.SendBossHit(boss, part, info);
        public bool HazardHit(Component hazard, in DamageInfo info) => NetStage.Instance != null && NetStage.Instance.SendHazardHit(hazard, info);
        public void PlayerHit(PlayerController remote, in DamageInfo info) => NetStage.Instance?.SendPlayerHit(remote, info);

        public bool EveryoneLoaded => !IsHost || ServerEveryoneLoaded;
        public void StageDone() => SendToServer(Msg.Done, null, FishNet.Transporting.Channel.Reliable);
        public void ReturnToVote() { if (IsHost) SendToServer(Msg.BackToVote, null, FishNet.Transporting.Channel.Reliable); }
        void ICoopBridge.Leave() => Leave();
    }
}
