// Play-mode self-check for the dialogue context (filter, "it", "them", "again", burner), using the real
// handlers with fake speech and pointing

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MMI.Fusion;

public partial class ParserIntent
{
    private class CheckSpeech : ISpeechInputSource
    {
        public event Action<SpeechWord> WordRecognized;
        public void Emit(string word) => WordRecognized?.Invoke(new SpeechWord(word));
    }

    private class CheckPointing : IPointingInputSource
    {
        public event Action<PointingSample> PointingChanged;
        public void Emit(PointingSample sample) => PointingChanged?.Invoke(sample);
    }

    public IEnumerator RunContextSelfCheck()
    {
        var speech = new CheckSpeech();
        var pointing = new CheckPointing();
        var engine = new FusionEngine(speech, pointing);
        InteractionSetup.DefineInteractions(engine);
        RegisterHandlers(engine);
        string fired = null;
        engine.IntentRecognized += i => fired = i.Action;

        int passed = 0, total = 0;
        var spawned = new List<GameObject>();
        void Check(string name, bool ok, string detail)
        {
            total++;
            if (ok) passed++;
            string line = $"[ContextSelfCheck] {name}: {(ok ? "PASS" : "FAIL")} — {detail}";
            if (ok) Debug.Log(line); else Debug.LogError(line);
        }
        // Returns true if the command ran and wasn't refused
        bool Say(string sentence, GameObject pointedAt = null, Vector3? hitPoint = null)
        {
            fired = null;
            _commandRefused = false;
            foreach (string w in sentence.Split(' ')) speech.Emit(w);
            engine.Tick(engine.BufferTimeoutSeconds + 0.01f); // end of sentence; longer would expire the pointing wait
            if (pointedAt != null)
            {
                Vector3 hit = hitPoint ?? GetPlacementBounds(pointedAt).center;
                Transform viewer = ViewerTransform();
                Vector3 origin = viewer != null ? viewer.position : hit - Vector3.forward;
                _targetRayOverride = new PointingRay(origin, (hit - origin).normalized, pointedAt, hit, Vector3.Distance(origin, hit));
                pointing.Emit(new PointingSample(pointedAt, hit));
                _targetRayOverride = null;
            }
            Debug.Log($"[ContextSelfCheck] \"{sentence}\" → {fired ?? "nothing"}{(_commandRefused ? " (refused)" : "")}");
            return fired != null && !_commandRefused;
        }

        Transform head = ViewerTransform();
        GameObject beakerPrefab = FindSpawnPrefab("beaker"), flaskPrefab = FindSpawnPrefab("flask");
        if (head == null || beakerPrefab == null || flaskPrefab == null)
        {
            Debug.LogError("[ContextSelfCheck] needs a camera and beaker/flask prefabs in Spawnable Prefabs.");
            yield break;
        }
        DeselectCurrent();
        _context.ClearFilter();
        Vector3 forward = FlatDirection(head, useRight: false), right = FlatDirection(head, useRight: true);

        // 1. Filter + pointing at a flask right next to a beaker: the beaker goes
        const float distance = 1.2f;
        Vector3 spot = head.position + forward * distance + Vector3.down * 0.35f;
        GameObject flask = SpawnTagged(flaskPrefab, null, "flask", spot);
        GameObject beaker = SpawnTagged(beakerPrefab, null, "beaker", spot + right * (distance * Mathf.Tan(selectAngleTolerance * 0.5f * Mathf.Deg2Rad)));
        spawned.Add(flask); spawned.Add(beaker);
        Physics.SyncTransforms();
        Say("consider only beakers");
        Say("delete this", flask);
        yield return null; // Destroy happens at the end of the frame
        Check("only beakers + delete this (flask next to a beaker)", beaker == null && flask != null,
              $"beaker {(beaker == null ? "deleted" : "still there")}, flask {(flask != null ? "stays" : "deleted")}");

        // 2. Same, pointing at a lone flask: refused
        GameObject lone = SpawnTagged(flaskPrefab, null, "flask", head.position + forward * 1.0f + Vector3.up * 0.7f - right * 0.6f);
        spawned.Add(lone);
        Physics.SyncTransforms();
        bool ran = Say("delete this", lone);
        yield return null;
        Check("only beakers + delete this (lone flask)", !ran && lone != null, ran ? "not refused" : "refused, flask stays");

        // 3. A colour narrows the current type
        Say("only beakers");
        Say("only red ones");
        Check("only beakers + only red ones", _context.FilterType == "beaker" && _context.FilterColor == "red", $"filter = {_context.Describe()}");

        // 4. A single create selects the new object, so "it" is that object
        Say("consider everything");
        Say("create a beaker");
        GameObject created = _selectedObject;
        if (created != null) spawned.Add(created);
        Say("make it red");
        VRObject createdVro = created != null ? created.GetComponent<VRObject>() : null;
        Check("create a beaker + make it red", createdVro != null && createdVro.objectType == "beaker" && createdVro.objectColor == "red",
              createdVro != null ? $"{created.name} is {createdVro.objectColor}" : "nothing selected after create");

        // 5. A row create becomes the last group, so "them" is the row
        Say("create three test tubes");
        var row = new List<GameObject>(_context.Group);
        spawned.AddRange(row);
        Say("make them blue");
        int blue = 0;
        foreach (GameObject t in row) if (t != null && t.GetComponent<VRObject>().objectColor == "blue") blue++;
        Check("create three test tubes + make them blue", row.Count == 3 && blue == 3, $"group {row.Count}, blue {blue}");

        // 6. "again" repeats the last command on the current target
        if (created != null)
        {
            SelectObject(created, null, null);
            FinishActiveMove(created);
            float yaw0 = created.transform.eulerAngles.y;
            Say("rotate left");
            Say("again");
            yield return new WaitForSeconds(rotateDuration + 0.2f);
            float turned = Mathf.DeltaAngle(yaw0, created.transform.eulerAngles.y);
            Check("rotate left + again", Mathf.Abs(turned + 2f * voiceRotateStep) < 1f, $"turned {turned:F1}° (expected {-2f * voiceRotateStep:F0}°)");
        }
        else Check("rotate left + again", false, "no created beaker to rotate");

        // 7. Clearing the filter restores the dimmed objects exactly
        Say("consider only flasks");
        Renderer dimmedRenderer = created != null ? created.GetComponentInChildren<MeshRenderer>() : null;
        bool wasDimmed = dimmedRenderer != null && dimmedRenderer.HasPropertyBlock();
        Say("consider everything");
        bool restored = dimmedRenderer != null && !dimmedRenderer.HasPropertyBlock() && _dimmedObjects.Count == 0;
        Check("consider everything", !_context.HasFilter && wasDimmed && restored,
              $"filter {(_context.HasFilter ? "still active" : "cleared")}, beaker dimmed under 'only flasks': {wasDimmed}, restored: {restored}");

        // 8. The burner as a command target (everything is put back afterwards)
        BunsenBurner burner = FindFirstObjectByType<BunsenBurner>();
        if (burner == null) Check("burner", false, "no BunsenBurner in the scene");
        else
        {
            GameObject b = burner.gameObject;
            DeselectCurrent();
            Say("consider everything");
            // Starting positions, so the selection hop and the moves can be undone
            Vector3 home = b.transform.position;
            var riders = new List<GameObject>(CollectBurnerRiders(b));
            var riderHomes = new List<Vector3>();
            foreach (GameObject r in riders) riderHomes.Add(r.transform.position);
            void RestoreBurner()
            {
                FinishActiveMove(b);
                b.transform.position = home;
                for (int i = 0; i < riders.Count; i++)
                    if (riders[i] != null) { FinishActiveMove(riders[i]); riders[i].transform.position = riderHomes[i]; }
                Physics.SyncTransforms();
            }

            bool burnerRan = Say("select the burner");
            Check("select the burner", burnerRan && _selectedObject == b, $"{(burnerRan ? "burnerRan" : "refused")}, selected {(_selectedObject != null ? _selectedObject.name : "nothing")}");
            DeselectCurrent();
            burnerRan = Say("pick the burner");
            Check("pick the burner", burnerRan && _selectedObject == b, $"{(burnerRan ? "burnerRan" : "refused")}, selected {(_selectedObject != null ? _selectedObject.name : "nothing")}");
            DeselectCurrent();
            burnerRan = Say("select that", b);
            RestoreBurner(); // undo the selection hop
            Check("select that (pointing at the burner)", burnerRan && _selectedObject == b, $"{(burnerRan ? "burnerRan" : "refused")}, selected {(_selectedObject != null ? _selectedObject.name : "nothing")}");

            burnerRan = Say("move to burner");
            Check("move to burner (burner selected)", fired == "moveToBurner" && !burnerRan, burnerRan ? "not refused" : "refused");

            // Destination: a free spot on the surface under the burner, beside it
            Bounds bb = GetPlacementBounds(b);
            if (TryFindSurfaceBelow(bb.center, bb.size.y + 1f, b, out float surfaceY, out Collider surface))
            {
                Vector3 besideBurner = new Vector3(bb.center.x, surfaceY, bb.center.z) + right * (bb.size.x + 0.15f);
                foreach (string sentence in new[] { "move it there", "move the burner there" })
                {
                    burnerRan = Say(sentence, surface.gameObject, besideBurner);
                    FinishActiveMove(b);
                    float moved = Vector3.Distance(b.transform.position, home);
                    int ridersAlong = 0;
                    for (int i = 0; i < riders.Count; i++)
                        if (riders[i] != null && Vector3.Distance(riders[i].transform.position - riderHomes[i], b.transform.position - home) < 0.01f) ridersAlong++;
                    Check($"{sentence} (burner selected)", fired != null && moved > 0.05f && ridersAlong == riders.Count,
                          $"{fired ?? "nothing"}, moved {moved:F2} m, {ridersAlong}/{riders.Count} rider(s) carried along");
                    RestoreBurner();
                }
            }
            else Check("move the burner", false, "no surface found under the burner");

            // A container still goes onto the stand
            GameObject onStand = SpawnTagged(beakerPrefab, null, "beaker", head.position + forward * 0.8f + right * 0.4f);
            spawned.Add(onStand);
            Physics.SyncTransforms();
            SelectObject(onStand, null, null);
            burnerRan = Say("put it on the burner");
            FinishActiveMove(onStand);
            Vector3 toStand = burner.Stand != null ? GetPlacementBounds(onStand).center - burner.Stand.position : Vector3.positiveInfinity;
            Check("put it on the burner (beaker selected)", burnerRan && Vector3.ProjectOnPlane(toStand, Vector3.up).magnitude < 0.05f,
                  $"{fired ?? "nothing"}, {(burnerRan ? "burnerRan" : "refused")}, {Vector3.ProjectOnPlane(toStand, Vector3.up).magnitude:F3} m from the stand");

            // On/off fire immediately (no pointing wait)
            bool wasOn = burner.IsOn;
            Say("turn on the burner");
            bool on = burner.IsOn;
            Say("turn off the burner");
            Check("turn on / off the burner", on && !burner.IsOn, $"on → {on}, off → {burner.IsOn}");
            if (wasOn) burner.TurnOn();

            // "delete everything" skips the burner. Filtered to burners so nothing else in the scene is touched.
            DeselectCurrent();
            Say("consider only burners");
            Say("delete everything");
            yield return null;
            Check("delete everything (only burners)", b != null, b != null ? "burner stays" : "burner deleted");
            Say("consider everything");
        }

        DeselectCurrent();
        _suppressDeleteSound = true;
        try { foreach (GameObject go in spawned) if (go != null) DeleteObject(go); }
        finally { _suppressDeleteSound = false; }
        _context.ClearFilter();
        RefreshContextVisuals();

        string summary = $"[ContextSelfCheck] {passed}/{total} passed";
        if (passed == total) Debug.Log(summary); else Debug.LogError(summary);
    }
}
