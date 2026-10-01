using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Rewards screen shown between BattlePhase and DecisionPhase. Reads
/// GameStateManager.LastBattleReport and plays: extracted units run onto an
/// exfil pad and cheer, each gets a card with an XP bar that overflows into
/// level-ups (confetti, shake, flash, stat deltas), fallen units get a muted
/// memorial card, then everyone beams out and we continue to DecisionPhase.
/// The 3D stage (ground, pads, light) is built here at runtime so the scene
/// only needs this component and a UIDocument.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AfterActionController : MonoBehaviour
{
    [SerializeField] private string nextSceneName = "DecisionPhase";

    [Header("Stage")]
    [SerializeField] private Vector3 cameraPosition = new Vector3(0f, 2.4f, -8.5f);
    [SerializeField] private Vector3 cameraLookAt = new Vector3(0f, -0.1f, 0f);
    [SerializeField] private float cameraFov = 38f;
    [SerializeField] private float unitSpacing = 2.1f;
    [SerializeField] private float runInDistance = 12f;
    [SerializeField] private float runSpeed = 7f;
    [SerializeField] private Color padColor = new Color(0f, 0.95f, 1f);

    [Header("Timing")]
    [SerializeField] private float fillSecondsPer100 = 1.1f;
    [SerializeField] private float holdAfterCard = 0.7f;

    // UI
    private VisualElement _root, _shake, _card, _icon, _xpFill, _confetti, _flash;
    private Label _title, _eyebrow, _name, _level, _badge, _xpLabel, _statHp, _statAtk, _statDef, _note, _hint;
    private Button _continue;

    // Stage
    private class Actor
    {
        public UnitReportEntry Entry;
        public GameObject Go;
        public Animator Anim;
        public Vector3 Pad;
        public GameObject PadGo;
    }
    private readonly List<Actor> _actors = new List<Actor>();
    private bool _skip;
    private bool _continueClicked;

    private void OnEnable()
    {
        _root = GetComponent<UIDocument>().rootVisualElement;
        _shake = _root.Q("aa-shake");
        _card = _root.Q("aa-card");
        _icon = _root.Q("aa-icon");
        _xpFill = _root.Q("aa-xp-fill");
        _confetti = _root.Q("aa-confetti");
        _flash = _root.Q("aa-flash");
        _title = _root.Q<Label>("aa-title");
        _eyebrow = _root.Q<Label>("aa-eyebrow");
        _name = _root.Q<Label>("aa-name");
        _level = _root.Q<Label>("aa-level");
        _badge = _root.Q<Label>("aa-badge");
        _xpLabel = _root.Q<Label>("aa-xp-label");
        _statHp = _root.Q<Label>("aa-stat-hp");
        _statAtk = _root.Q<Label>("aa-stat-atk");
        _statDef = _root.Q<Label>("aa-stat-def");
        _note = _root.Q<Label>("aa-note");
        _hint = _root.Q<Label>("aa-hint");
        _continue = _root.Q<Button>("aa-continue");

        _root.RegisterCallback<PointerDownEvent>(OnPointerDown);
        if (_continue != null) _continue.clicked += OnContinueClicked;
    }

    private void OnDisable()
    {
        _root?.UnregisterCallback<PointerDownEvent>(OnPointerDown);
        if (_continue != null) _continue.clicked -= OnContinueClicked;
    }

    private void OnPointerDown(PointerDownEvent evt) => _skip = true;
    private void OnContinueClicked() => _continueClicked = true;

    private void Start()
    {
        var gsm = GameStateManager.Instance;
        BattleReport report = gsm != null ? gsm.LastBattleReport : null;
        if (report == null || report.IsEmpty)
        {
            // Opened directly / nothing to show: don't strand the player here.
            SceneManager.LoadScene(nextSceneName);
            return;
        }

        gsm.ClearPendingLevelUps(); // this screen replaces the planning-phase level-up cards
        StartCoroutine(Run(report));
    }

    // ------------------------------------------------------------------
    // Main sequence
    // ------------------------------------------------------------------

    private IEnumerator Run(BattleReport report)
    {
        var extracted = new List<UnitReportEntry>();
        var fallen = new List<UnitReportEntry>();
        foreach (var e in report.Entries)
        {
            if (e.Outcome == BattleOutcome.Died) fallen.Add(e);
            else extracted.Add(e); // anything still "InField" at the end counts as having made it out
        }

        bool won = extracted.Count > 0;
        _title.text = won ? "MISSION COMPLETE" : "ALL HANDS LOST";
        _title.EnableInClassList("aa-title--loss", !won);
        _card.style.opacity = 0f;
        _badge.style.opacity = 0f;

        BuildStage();
        SpawnActors(extracted);

        yield return UIAnim.PunchIn(_title.parent, 0.45f);
        yield return RunIn();

        foreach (var actor in _actors)
        {
            yield return ShowRewardCard(actor.Entry, actor, false);
        }
        foreach (var entry in fallen)
        {
            yield return ShowRewardCard(entry, null, true);
        }

        // Done: offer Continue, then beam everyone out.
        _hint.AddToClassList("hidden");
        _continue.RemoveFromClassList("hidden");
        StartCoroutine(UIAnim.PunchIn(_continue, 0.35f));
        _continueClicked = false;
        while (!_continueClicked) yield return null;

        _continue.SetEnabled(false);
        yield return BeamOut();
        yield return UIAnim.Flash(_flash, 1f, 0.35f);
        SceneManager.LoadScene(nextSceneName);
    }

    private IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds && !_skip; t += Time.unscaledDeltaTime)
            yield return null;
    }

    // ------------------------------------------------------------------
    // Reward card
    // ------------------------------------------------------------------

    private IEnumerator ShowRewardCard(UnitReportEntry e, Actor actor, bool fallen)
    {
        _skip = false;
        Unit u = e.Unit;
        Color rarity = UnitRarityTable.GetColor(u.Rarity);

        _card.EnableInClassList("aa-card--fallen", fallen);
        _card.style.borderBottomColor = _card.style.borderTopColor =
            _card.style.borderLeftColor = _card.style.borderRightColor = fallen ? new Color(0.35f, 0.38f, 0.41f) : rarity;
        _name.text = u.UnitName.ToUpperInvariant();
        _name.style.color = fallen ? new StyleColor(StyleKeyword.Null) : new StyleColor(rarity);
        _icon.style.backgroundImage = u.UnitIcon != null ? new StyleBackground(u.UnitIcon) : new StyleBackground();
        _level.text = $"LV. {e.StartLevel}";
        _level.RemoveFromClassList("aa-level--pop");
        _badge.style.opacity = 0f;
        _xpFill.RemoveFromClassList("aa-xp-fill--full");

        int xp = e.StartXP;
        SetXp(xp);
        SetStats(e, e.StartMaxHP, e.StartAttack, e.StartDefense);

        int gained = e.LevelSteps.Count * UnitInstance.ExperiencePerLevel + e.EndXP - e.StartXP;
        _note.text = fallen ? "Fell in battle. They will be remembered."
                            : (gained > 0 ? $"+{gained} XP" : "No XP earned this time.")
                              + (e.RescuedVillagers > 0 ? $"   •   {e.RescuedVillagers} townsfolk rescued" : "");

        if (actor != null) StartCoroutine(Hop(actor, 0.35f, 0.6f));
        yield return UIAnim.PunchIn(_card, 0.32f);
        if (fallen)
        {
            yield return Wait(holdAfterCard * 2.2f);
            yield return UIAnim.FadeTo(_card, 0f, 0.25f);
            yield break;
        }
        yield return Wait(0.25f);

        // XP overflows into one level-up per step, then settles on the end value.
        int curHp = e.StartMaxHP, curAtk = e.StartAttack, curDef = e.StartDefense;
        foreach (LevelStep step in e.LevelSteps)
        {
            yield return FillTo(xp, UnitInstance.ExperiencePerLevel);
            _xpFill.AddToClassList("aa-xp-fill--full");

            curHp = step.NewMaxHP; curAtk = step.NewAttack; curDef = step.NewDefense;
            _level.text = $"LV. {e.StartLevel} → LV. {step.NewLevel}";
            _level.AddToClassList("aa-level--pop");
            SetStats(e, curHp, curAtk, curDef);

            StartCoroutine(LevelUpFx(actor));
            yield return Wait(0.9f);

            xp = 0;
            _xpFill.RemoveFromClassList("aa-xp-fill--full");
            SetXp(0);
            yield return Wait(0.15f);
        }
        yield return FillTo(xp, e.EndXP);

        yield return Wait(holdAfterCard);
        yield return UIAnim.FadeTo(_card, 0f, 0.2f);
    }

    private IEnumerator LevelUpFx(Actor actor)
    {
        StartCoroutine(UIAnim.Flash(_flash, 0.6f, 0.4f));
        StartCoroutine(UIAnim.Shake(_shake, 14f, 0.4f));
        StartCoroutine(UIAnim.PunchIn(_badge, 0.35f));
        StartCoroutine(UIAnim.Pop(_level, 1.4f, 0.35f));
        if (actor != null) StartCoroutine(Hop(actor, 0.9f, 0.5f));

        Vector2 origin = _confetti.WorldToLocal(_card.worldBound.center);
        yield return UIAnim.Confetti(_confetti, origin);
    }

    private IEnumerator FillTo(int from, int to)
    {
        int cap = UnitInstance.ExperiencePerLevel;
        float dur = Mathf.Max(0.15f, Mathf.Abs(to - from) / (float)cap * fillSecondsPer100);
        for (float t = 0f; t < dur && !_skip; t += Time.unscaledDeltaTime)
        {
            SetXp(Mathf.RoundToInt(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur))));
            yield return null;
        }
        SetXp(to);
    }

    private void SetXp(int xp)
    {
        int cap = UnitInstance.ExperiencePerLevel;
        _xpFill.style.width = new Length(Mathf.Clamp01(xp / (float)cap) * 100f, LengthUnit.Percent);
        _xpLabel.text = $"XP {xp} / {cap}";
    }

    private void SetStats(UnitReportEntry e, int hp, int atk, int def)
    {
        SetStat(_statHp, "HP", e.StartMaxHP, hp);
        SetStat(_statAtk, "ATK", e.StartAttack, atk);
        SetStat(_statDef, "DEF", e.StartDefense, def);
    }

    private static void SetStat(Label label, string tag, int from, int to)
    {
        bool up = to > from;
        label.text = up ? $"{tag}  {from} → {to}  (+{to - from})" : $"{tag}  {to}";
        label.EnableInClassList("aa-stat--gain", up);
    }

    // ------------------------------------------------------------------
    // 3D stage
    // ------------------------------------------------------------------

    private void BuildStage()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("AfterActionCamera") { tag = "MainCamera" };
            cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.08f, 0.11f);
        cam.fieldOfView = cameraFov;
        cam.transform.position = cameraPosition;
        cam.transform.LookAt(cameraLookAt);

        if (FindAnyObjectByType<Light>() == null)
        {
            var lightGo = new GameObject("AfterActionLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.65f);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        Destroy(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(0f, -0.55f, 0f);
        ground.transform.localScale = new Vector3(30f, 1f, 30f);
        ground.GetComponent<Renderer>().material = MakeMaterial(new Color(0.09f, 0.12f, 0.16f));
    }

    private void SpawnActors(List<UnitReportEntry> extracted)
    {
        int n = extracted.Count;
        for (int i = 0; i < n; i++)
        {
            UnitReportEntry e = extracted[i];
            var actor = new Actor
            {
                Entry = e,
                Pad = new Vector3((i - (n - 1) * 0.5f) * unitSpacing, 0f, 0f)
            };

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "ExfilPad";
            Destroy(pad.GetComponent<Collider>());
            pad.transform.position = actor.Pad + Vector3.down * 0.02f;
            pad.transform.localScale = new Vector3(1.5f, 0.02f, 1.5f);
            pad.GetComponent<Renderer>().material = MakeMaterial(padColor);
            actor.PadGo = pad;

            GameObject prefab = e.Unit.Archetype != null ? e.Unit.Archetype.ModelPrefab : null;
            if (prefab != null)
            {
                Vector3 start = actor.Pad + Vector3.forward * runInDistance;
                actor.Go = Instantiate(prefab, start, Quaternion.LookRotation(Vector3.up));
                actor.Go.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // face the camera
                var inst = actor.Go.GetComponent<UnitInstance>();
                if (inst != null)
                {
                    inst.Initialize(e.Unit); // weapons + color scheme
                    inst.enabled = false;    // purely visual here
                }
                actor.Anim = actor.Go.GetComponentInChildren<Animator>(true);
                UnitInstance instance = actor.Go.GetComponent<UnitInstance>();
                instance.playSpawnAnimation();
                instance.playExfiltrationAnimation();

                if (actor.Anim != null) actor.Anim.applyRootMotion = false;
            }
            _actors.Add(actor);
        }
    }

    private IEnumerator RunIn()
    {
        foreach (var a in _actors) SetBool(a.Anim, "Walk", a.Go != null);

        bool moving = true;
        float elapsed = 0f;
        while (moving && !_skip)
        {
            moving = false;
            elapsed += Time.unscaledDeltaTime;
            for (int i = 0; i < _actors.Count; i++)
            {
                Actor a = _actors[i];
                if (a.Go == null || elapsed < i * 0.18f) { moving |= a.Go != null; continue; }
                a.Go.transform.position = Vector3.MoveTowards(a.Go.transform.position, a.Pad, runSpeed * Time.unscaledDeltaTime);
                if ((a.Go.transform.position - a.Pad).sqrMagnitude > 0.0004f) moving = true;
            }
            yield return null;
        }

        foreach (var a in _actors)
        {
            if (a.Go == null) continue;
            a.Go.transform.position = a.Pad;
            SetBool(a.Anim, "Walk", false);
            SetBool(a.Anim, "Evacuate", true); // drives the Cheering state in Character.controller
        }
        yield return Wait(0.3f);
    }

    private IEnumerator Hop(Actor a, float height, float duration)
    {
        if (a.Go == null) yield break;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            if (a.Go == null) yield break;
            a.Go.transform.position = a.Pad + Vector3.up * (Mathf.Sin(t / duration * Mathf.PI) * height);
            yield return null;
        }
        if (a.Go != null) a.Go.transform.position = a.Pad;
    }

    /// <summary>Light pillars rise from every pad and the units are carried up out of frame.</summary>
    private IEnumerator BeamOut()
    {
        var beams = new List<Transform>();
        foreach (var a in _actors)
        {
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(beam.GetComponent<Collider>());
            beam.transform.position = a.Pad;
            beam.transform.localScale = new Vector3(1.1f, 0.01f, 1.1f);
            beam.GetComponent<Renderer>().material = MakeMaterial(Color.Lerp(padColor, Color.white, 0.5f));
            beams.Add(beam.transform);
        }

        const float duration = 1.2f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float p = t / duration;
            for (int i = 0; i < _actors.Count; i++)
            {
                float local = Mathf.Clamp01((p * duration - i * 0.12f) / (duration - 0.12f * _actors.Count));
                float beamH = Mathf.Lerp(0.01f, 12f, Mathf.Min(1f, local * 3f));
                beams[i].localScale = new Vector3(1.1f, beamH, 1.1f);
                beams[i].position = _actors[i].Pad + Vector3.up * beamH;

                if (_actors[i].Go != null)
                {
                    float lift = local * local * 14f;
                    _actors[i].Go.transform.position = _actors[i].Pad + Vector3.up * lift;
                    _actors[i].Go.transform.Rotate(0f, 720f * Time.unscaledDeltaTime * local, 0f);
                }
            }
            yield return null;
        }
        foreach (var a in _actors) if (a.Go != null) Destroy(a.Go);
    }

    private static void SetBool(Animator anim, string param, bool value)
    {
        if (anim == null) return;
        foreach (var p in anim.parameters)
        {
            if (p.name == param && p.type == AnimatorControllerParameterType.Bool)
            {
                anim.SetBool(param, value);
                return;
            }
        }
    }

    private static Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        return mat;
    }
}
