using System;
using System.Text;
using FishNet.Broadcast;
using UnityEngine;

namespace ElderTree.Net
{
    /// <summary>
    /// The one message type that crosses the wire. Every co-op message is a kind, who sent it, who it is for
    /// (-1: everyone but the sender) and an opaque payload written with <see cref="NetWriter"/>. The server only routes;
    /// game logic lives on the peers (the host's peer runs the bosses).
    /// </summary>
    public struct CoopPacket : IBroadcast
    {
        public byte kind;
        public int from;
        public int to;
        public byte[] data;
    }

    /// <summary>Message kinds. Values are part of the wire format: append, never renumber.</summary>
    public enum Msg : byte
    {
        // server -> everyone (session state)
        Roster = 1,          // the party: id, slot, name, host flag
        Phase = 2,           // lobby / vote / stage (+ scene, boss)
        VoteState = 3,       // each player's vote, the countdown, the pick
        // peer -> server
        Hello = 10,          // my name
        Vote = 11,           // my vote (stage index, Random, or none)
        StartVote = 12,      // host: from the party room to the vote
        Loaded = 13,         // I have the stage loaded
        Done = 14,           // I am done with the stage (after a victory)
        BackToVote = 15,     // host: everyone back to the vote
        // peer -> peers (relayed)
        PlayerState = 20,    // my tree: transform, state, health (unreliable)
        PlayerPose = 21,     // my tree's bones (unreliable, chunked)
        BossState = 22,      // host: each boss's numbers and aggro (unreliable)
        BossPose = 23,       // host: a boss's rig and what it spawned (unreliable, chunked)
        BossEvent = 24,      // host: spawn / felled / end / wipe / damaged (reliable)
        HitPlayer = 25,      // host -> owner: a boss struck your tree
        HitBoss = 26,        // client -> host: I struck the boss
        HitHazard = 27,      // client -> host: I struck something the boss spawned
        Fx = 28,             // a particle effect went off (unreliable)
        Sfx = 29,            // a sound played (unreliable)
        Blast = 30,          // host: an explosion (each player shakes their own camera)
    }

    public enum BossEv : byte { Spawn = 1, Felled = 2, EndFight = 3, Wipe = 4, Damaged = 5 }

    /// <summary>Little-endian payload writer. Reused: <see cref="Reset"/> then write, then <see cref="ToArray"/>.</summary>
    public sealed class NetWriter
    {
        byte[] buf = new byte[512];
        public int Length { get; private set; }

        public NetWriter Reset() { Length = 0; return this; }

        void Need(int n)
        {
            if (Length + n <= buf.Length) return;
            Array.Resize(ref buf, Math.Max(buf.Length * 2, Length + n));
        }

        public byte[] ToArray()
        {
            var a = new byte[Length];
            Buffer.BlockCopy(buf, 0, a, 0, Length);
            return a;
        }

        public void Byte(byte v) { Need(1); buf[Length++] = v; }
        public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);
        public void UShort(ushort v) { Need(2); buf[Length++] = (byte)v; buf[Length++] = (byte)(v >> 8); }
        public void Short(short v) => UShort((ushort)v);
        public void Int(int v) { Need(4); buf[Length++] = (byte)v; buf[Length++] = (byte)(v >> 8); buf[Length++] = (byte)(v >> 16); buf[Length++] = (byte)(v >> 24); }
        public void UInt(uint v) => Int((int)v);
        public void Float(float v) => Int(BitConverter.SingleToInt32Bits(v));
        public void Half(float v) => UShort(Mathf.FloatToHalf(v));
        public void Vec3(Vector3 v) { Float(v.x); Float(v.y); Float(v.z); }
        public void HalfVec3(Vector3 v) { Half(v.x); Half(v.y); Half(v.z); }
        /// <summary>A unit value in one byte (0..1).</summary>
        public void Unit(float v) => Byte((byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255));

        /// <summary>Smallest-three quaternion in 32 bits (~0.15 degree).</summary>
        public void Quat(Quaternion q) => UInt(PackQuat(q));

        public void Str(string s)
        {
            if (string.IsNullOrEmpty(s)) { UShort(0); return; }
            var b = Encoding.UTF8.GetBytes(s);
            int n = Math.Min(b.Length, 1024);
            UShort((ushort)n);
            Need(n);
            Buffer.BlockCopy(b, 0, buf, Length, n);
            Length += n;
        }

        public void Bytes(byte[] b, int offset, int count)
        {
            Need(count);
            Buffer.BlockCopy(b, offset, buf, Length, count);
            Length += count;
        }

        /// <summary>Overwrite a byte already written (counts patched in after the fact).</summary>
        public void PatchByte(int at, byte v) => buf[at] = v;
        public void PatchUShort(int at, ushort v) { buf[at] = (byte)v; buf[at + 1] = (byte)(v >> 8); }

        const float QMax = 0.70710678f;

        public static uint PackQuat(Quaternion q)
        {
            float ax = Mathf.Abs(q.x), ay = Mathf.Abs(q.y), az = Mathf.Abs(q.z), aw = Mathf.Abs(q.w);
            int largest = 0;
            float m = ax;
            if (ay > m) { largest = 1; m = ay; }
            if (az > m) { largest = 2; m = az; }
            if (aw > m) { largest = 3; }
            float sign = q[largest] < 0f ? -1f : 1f;
            uint packed = (uint)largest << 30;
            int shift = 20;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                float v = q[i] * sign;
                uint qv = (uint)Mathf.Clamp(Mathf.RoundToInt((v + QMax) / (2f * QMax) * 1023f), 0, 1023);
                packed |= qv << shift;
                shift -= 10;
            }
            return packed;
        }

        public static Quaternion UnpackQuat(uint packed)
        {
            int largest = (int)(packed >> 30);
            var q = new Quaternion();
            float sum = 0f;
            int shift = 20;
            for (int i = 0; i < 4; i++)
            {
                if (i == largest) continue;
                float v = ((packed >> shift) & 1023u) / 1023f * (2f * QMax) - QMax;
                q[i] = v;
                sum += v * v;
                shift -= 10;
            }
            q[largest] = Mathf.Sqrt(Mathf.Max(0f, 1f - sum));
            return q;
        }
    }

    /// <summary>Reads what <see cref="NetWriter"/> wrote. Out-of-range reads return zeros (a short packet never throws).</summary>
    public struct NetReader
    {
        readonly byte[] d;
        int p;

        public NetReader(byte[] data) { d = data ?? Array.Empty<byte>(); p = 0; }
        public bool More => p < d.Length;
        public int Position => p;

        public byte Byte() => p < d.Length ? d[p++] : (byte)0;
        public bool Bool() => Byte() != 0;
        public ushort UShort() { if (p + 2 > d.Length) { p = d.Length; return 0; } ushort v = (ushort)(d[p] | d[p + 1] << 8); p += 2; return v; }
        public short Short() => (short)UShort();
        public int Int() { if (p + 4 > d.Length) { p = d.Length; return 0; } int v = d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24; p += 4; return v; }
        public uint UInt() => (uint)Int();
        public float Float() => BitConverter.Int32BitsToSingle(Int());
        public float Half() => Mathf.HalfToFloat(UShort());
        public Vector3 Vec3() => new Vector3(Float(), Float(), Float());
        public Vector3 HalfVec3() => new Vector3(Half(), Half(), Half());
        public float Unit() => Byte() / 255f;
        public Quaternion Quat() => NetWriter.UnpackQuat(UInt());

        public string Str()
        {
            int n = UShort();
            if (n == 0 || p + n > d.Length) { p = Math.Min(p + n, d.Length); return ""; }
            var s = Encoding.UTF8.GetString(d, p, n);
            p += n;
            return s;
        }
    }
}
