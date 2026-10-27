using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Victory screen loaded from BattlePhase when the run is won. The surviving heroes run onto
/// exfil pads and cheer while confetti falls over the final score card. Play Again / Title
/// use GameStateManager like the loss panel does. The 3D stage (ground, pads, light) is
/// built here at runtime so the scene only needs this component and a UIDocument.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class AfterActionController : MonoBehaviour
{
    [Header("Stage")]
    [SerializeField] private Vector3 cameraPosition = new Vector3(0f, 2.4f, -8.5f);
    [SerializeField] private Vector3 cameraLookAt = new Vector3(0f, -0.1f, 0f);
    [SerializeField] private float cameraFov = 38f;
    [SerializeField] private float unitSpacing = 2.1f;
    [SerializeField] private float runInDistance = 12f;
    [SerializeField] private float runSpeed = 7f;
    [SerializeField] private Color padColor = new Color(0f, 0.95f, 1f);

    [Header("Confetti")]
    [SerializeField] private float confettiInterval = 1.4f;

    private VisualElement _root, _shake, _card, _confetti, _flash;
    private Label _title, _eyebrow, _name, _level, _statHp, _statAtk, _statDef, _note, _hint;
    private Button _continue;

    private class Actor
    {
        public GameObject Go;
        public Animator Anim;
        public Vector3 Pad;
    }
    private readonly List<Actor> _actors = new List<Actor>();

    private void OnEnable()
    {
        _root = GetComponent<UIDocument>().rootVisualElement;
        _shake = _root.Q("aa-shake");
        _card = _root.Q("aa-card");
        _confetti = _root.Q("aa-confetti");
        _flash = _root.Q("aa-flash");
        _title = _root.Q<Label>("aa-title");
        _eyebrow = _root.Q<Label>("aa-eyebrow");
        _name = _root.Q<Label>("aa-name");
        _level = _root.Q<Label>("aa-level");
        _statHp = _root.Q<Label>("aa-stat-hp");
        _statAtk = _root.Q<Label>("aa-stat-atk");
        _statDef = _root.Q<Label>("aa-stat-def");
        _note = _root.Q<Label>("aa-note");
        _hint = _root.Q<Label>("aa-hint");
        _continue = _root.Q<Button>("aa-continue");
    }

    private void Start()
    {
        GameStateManager gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            // Opened directly with no run to celebrate: don't strand the player here.
            UnityEngine.SceneManagement.SceneManager.LoadScene(GameStateManager.TitleSceneName);
            return;
        }

        StartCoroutine(Run(gsm));
    }

    private IEnumerator Run(GameStateManager gsm)
    {
        GameStateManager.GameResult r = gsm.LastResult;

        _eyebrow.text = "VICTORY";
        _title.text = "YOU ESCAPED";
        _name.text = $"SCORE  {r.Score:N0}";
        _level.text = $"{r.VillagersEscaped} villagers made it to safety";
        _statHp.text = $"ROUNDS  {r.Rounds}";
        _statAtk.text = $"ENEMIES DEFEATED  {r.EnemiesKilled}";
        _statDef.text = $"HEROES LOST  {r.HeroesLost}   VILLAGERS LOST  {r.VillagersLost}";
        _note.text = "";
        _card.Q(className: "aa-xp-track")?.EnableInClassList("hidden", true);
        _card.Q("aa-icon")?.EnableInClassList("hidden", true);
        _card.Q("aa-badge")?.EnableInClassList("hidden", true);
        _card.style.opacity = 0f;

        _hint.text = "";
        _continue.text = "PLAY AGAIN";
        _continue.RemoveFromClassList("hidden");
        _continue.clicked += () => gsm.RestartGame();
        var titleButton = new Button(() => gsm.ReturnToTitle()) { text = "TITLE" };
        titleButton.AddToClassList("aa-continue");
        _continue.parent.Add(titleButton);

        BuildStage();
        SpawnActors(gsm);

        yield return UIAnim.PunchIn(_title.parent, 0.45f);
        yield return RunIn();

        yield return UIAnim.PunchIn(_card, 0.32f);
        yield return UIAnim.Flash(_flash, 0.6f, 0.4f);

        // Keep the party going until the player leaves.
        while (true)
        {
            Vector2 origin = _confetti.WorldToLocal(_card.worldBound.center);
            StartCoroutine(UIAnim.Confetti(_confetti, origin));
            yield return UIAnim.Pop(_title, 1.15f, 0.3f);
            yield return new WaitForSecondsRealtime(confettiInterval);
        }
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

    // Surviving heroes only: villagers are the ones who left, heroes never leave the map.
    private void SpawnActors(GameStateManager gsm)
    {
        var heroes = new List<Unit>();
        foreach (Unit unit in gsm.ActiveTeam)
            if (unit != null && unit.CurrentHP > 0) heroes.Add(unit);

        int n = heroes.Count;
        for (int i = 0; i < n; i++)
        {
            Unit unit = heroes[i];
            var actor = new Actor { Pad = new Vector3((i - (n - 1) * 0.5f) * unitSpacing, 0f, 0f) };

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = "ExfilPad";
            Destroy(pad.GetComponent<Collider>());
            pad.transform.position = actor.Pad + Vector3.down * 0.02f;
            pad.transform.localScale = new Vector3(1.5f, 0.02f, 1.5f);
            pad.GetComponent<Renderer>().material = MakeMaterial(padColor);

            GameObject prefab = unit.Archetype != null ? unit.Archetype.ModelPrefab : null;
            if (prefab != null)
            {
                actor.Go = Instantiate(prefab, actor.Pad + Vector3.forward * runInDistance, Quaternion.Euler(0f, 180f, 0f));
                var inst = actor.Go.GetComponent<UnitInstance>();
                if (inst != null)
                {
                    inst.Initialize(unit); // weapons + color scheme
                    inst.enabled = false;  // purely visual here
                    inst.playSpawnAnimation();
                    inst.playExfiltrationAnimation();
                }
                actor.Anim = actor.Go.GetComponentInChildren<Animator>(true);
                if (actor.Anim != null) actor.Anim.applyRootMotion = false;
            }
            _actors.Add(actor);
        }
    }

    private IEnumerator RunIn()
    {
        foreach (Actor a in _actors) SetBool(a.Anim, "Walk", a.Go != null);

        bool moving = true;
        float elapsed = 0f;
        while (moving)
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

        foreach (Actor a in _actors)
        {
            if (a.Go == null) continue;
            a.Go.transform.position = a.Pad;
            SetBool(a.Anim, "Walk", false);
            SetBool(a.Anim, "Evacuate", true); // drives the Cheering state in Character.controller
        }
    }

    private static void SetBool(Animator anim, string param, bool value)
    {
        if (anim == null) return;
        foreach (AnimatorControllerParameter p in anim.parameters)
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
