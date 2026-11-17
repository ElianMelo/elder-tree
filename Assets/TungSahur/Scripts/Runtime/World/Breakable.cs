using System.Collections;
using System.Collections.Generic;
using TungSahur.Audio;
using TungSahur.CameraSystem;
using TungSahur.Combat;
using TungSahur.FX;
using UnityEngine;

namespace TungSahur.World
{
    /// <summary>
    /// A stone boulder or cracked wall. Only blows with enough break power (the Alligator Mask's bomb
    /// hammer, cannon shells and Sky Bombardment) shatter it into tumbling chunks; anything weaker
    /// clanks off with sparks. <see cref="RestoreAll"/> puts every obstacle back (debug menu).
    /// </summary>
    public class Breakable : MonoBehaviour, IDamageable
    {
        public string displayName = "Boulder";
        [Tooltip("Minimum DamageInfo.breakPower that shatters it.")]
        public int requiredPower = 1;
        [Tooltip("Hits with enough power it takes to break (cracked walls: 1).")]
        public int hitsToBreak = 1;
        public GameObject intact;
        public Collider[] solidColliders;
        public GameObject[] chunkPrefabs;
        public int chunkCount = 10;
        public Vector3 chunkArea = new Vector3(1.6f, 1.4f, 1.4f);
        public Vector3 chunkCenter = new Vector3(0f, 0.8f, 0f);
        public float chunkScale = 1f;
        public float chunkLifetime = 6f;

        public static readonly List<Breakable> All = new();
        public bool Broken { get; private set; }
        public bool IsAlive => !Broken;
        public Transform Root => transform;
        public event System.Action<Breakable> Shattered;

        int hitsTaken;
        float lastClank = -9f;
        readonly List<GameObject> live = new();

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDestroy() => All.Remove(this);

        public bool ReceiveHit(in DamageInfo info)
        {
            if (Broken) return false;
            if (info.breakPower < requiredPower)
            {
                // too weak: clank, sparks, a little shake so it reads as "not like this"
                if (Time.time - lastClank > 0.12f)
                {
                    lastClank = Time.time;
                    FXManager.Spawn(FXType.HitSpark, info.point, Quaternion.LookRotation(-info.direction + Vector3.up * 0.4f), 0.9f);
                    FXManager.Spawn(FXType.DustSmall, info.point, Quaternion.identity, 0.6f);
                    AudioManager.Play(Sfx.Deflect, info.point, 0.9f, Random.Range(0.92f, 1.05f));
                    if (!info.explosive) CameraDirector.Instance?.Shake(0.12f, -info.direction);
                }
                return false;
            }
            hitsTaken++;
            if (hitsTaken < hitsToBreak)
            {
                FXManager.Spawn(FXType.SlamDebris, info.point, Quaternion.identity, 0.6f);
                AudioManager.Play(Sfx.RockBreak, info.point, 0.6f, 1.2f);
                return true;
            }
            Shatter(info.point, info.direction);
            return true;
        }

        void Shatter(Vector3 point, Vector3 dir)
        {
            Broken = true;
            if (intact) intact.SetActive(false);
            foreach (var c in solidColliders) if (c) c.enabled = false;
            foreach (var hb in GetComponentsInChildren<Hurtbox>()) { var c = hb.GetComponent<Collider>(); if (c) c.enabled = false; }

            Vector3 center = transform.TransformPoint(chunkCenter);
            FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 2.2f * chunkScale);
            FXManager.Spawn(FXType.SlamDebris, center, Quaternion.identity, 1.6f);
            FXManager.Spawn(FXType.Smoke, center, Quaternion.identity, 0.8f);
            AudioManager.Play(Sfx.RockBreak, center, 1f, Random.Range(0.9f, 1.02f));
            CameraDirector.Instance?.Shake(0.45f, Vector3.down);

            if (chunkPrefabs != null && chunkPrefabs.Length > 0)
            {
                dir.y = 0f;
                for (int i = 0; i < chunkCount; i++)
                {
                    var prefab = chunkPrefabs[i % chunkPrefabs.Length];
                    Vector3 local = chunkCenter + Vector3.Scale(chunkArea, new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f));
                    Vector3 p = transform.TransformPoint(local);
                    var go = Instantiate(prefab, p, Random.rotation);
                    go.transform.localScale *= chunkScale * Random.Range(0.7f, 1.25f);
                    go.layer = 2;   // ignore raycast: never blocks aim or the motor's ground probe
                    var rb = go.GetComponent<Rigidbody>() ?? go.AddComponent<Rigidbody>();
                    if (go.GetComponent<Collider>() == null)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.convex = true;
                        mc.sharedMesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
                    }
                    rb.mass = 30f;
                    Vector3 away = (p - point); away.y = 0f;
                    away = away.sqrMagnitude > 1e-3f ? away.normalized : Random.insideUnitSphere;
                    rb.linearVelocity = (away * Random.Range(2.5f, 6f) + dir.normalized * 2f + Vector3.up * Random.Range(3f, 7f));
                    rb.angularVelocity = Random.insideUnitSphere * 8f;
                    live.Add(go);
                    StartCoroutine(Sink(go, chunkLifetime + Random.Range(0f, 1.5f)));
                }
            }
            Shattered?.Invoke(this);
        }

        IEnumerator Sink(GameObject go, float after)
        {
            yield return new WaitForSeconds(after);
            if (go == null) yield break;
            var rb = go.GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            float t = 0f;
            while (go != null && t < 1.5f)
            {
                t += Time.deltaTime;
                go.transform.position += Vector3.down * Time.deltaTime * 0.4f;
                yield return null;
            }
            if (go != null) { live.Remove(go); Destroy(go); }
        }

        public void Restore()
        {
            StopAllCoroutines();
            foreach (var g in live) if (g) Destroy(g);
            live.Clear();
            Broken = false;
            hitsTaken = 0;
            if (intact) intact.SetActive(true);
            foreach (var c in solidColliders) if (c) c.enabled = true;
            foreach (var hb in GetComponentsInChildren<Hurtbox>(true)) { var c = hb.GetComponent<Collider>(); if (c) c.enabled = true; }
            if (gameObject.activeInHierarchy) FXManager.Spawn(FXType.Dust, transform.position, Quaternion.identity, 1.4f);
        }

        public static void RestoreAll()
        {
            foreach (var b in All) if (b != null) b.Restore();
        }
    }
}
