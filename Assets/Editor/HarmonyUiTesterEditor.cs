using UnityEditor;
using UnityEngine;

/// <summary>
/// <see cref="HarmonyUiTester"/> için Inspector arayüzü.
///
/// Play modunda gözlüğe build almadan panelleri denemek için tıklanabilir
/// butonlar çizer. Butonlar yalnızca Play modunda etkindir; edit modunda
/// enjekte edilen veri hiçbir şey yapmaz çünkü abone bileşenler çalışmıyordur.
/// </summary>
[CustomEditor(typeof(HarmonyUiTester))]
public class HarmonyUiTesterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var tester = (HarmonyUiTester)target;

        EditorGUILayout.Space(8);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Test butonları yalnızca Play modunda çalışır.\n" +
                "Play'e girip aşağıdaki butonları kullanın.",
                MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            EditorGUILayout.LabelField("1 · Komut Butonları", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Gerçek buton onClick zincirini tetikler; rosbridge bağlıysa " +
                "/harmony/cmd_input'a mesaj gider.", MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (Btn("START", new Color(0.06f, 0.72f, 0.51f))) tester.ClickStart();
                if (Btn("CONFIRM", new Color(0.06f, 0.72f, 0.51f))) tester.ClickConfirm();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (Btn("REINSPECT", new Color(0.39f, 0.40f, 0.95f))) tester.ClickReinspect();
                if (Btn("STOP", new Color(0.94f, 0.27f, 0.27f))) tester.ClickStop();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("2 · Görev Durumu", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (Btn("IDLE", new Color(0.58f, 0.64f, 0.72f))) tester.StateIdle();
                if (Btn("SR_MODE", new Color(0.02f, 0.71f, 0.83f))) tester.StateSensing();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (Btn("WAITING (banner)", new Color(0.96f, 0.62f, 0.04f))) tester.StateWaiting();
                if (Btn("CR_MODE", new Color(0.66f, 0.33f, 0.97f))) tester.StateCleaning();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("3 · Kusur Tablosu", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (Btn("4 kusur gönder", new Color(0.02f, 0.71f, 0.83f))) tester.PushAllDefects();
                if (Btn("Tabloyu temizle", new Color(0.45f, 0.45f, 0.5f))) tester.ClearDefects();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (Btn("Sıradaki → CLEANING", new Color(0.96f, 0.62f, 0.04f))) tester.MarkNextCleaning();
                if (Btn("Sıradaki → CLEANED", new Color(0.06f, 0.72f, 0.51f))) tester.MarkNextCleaned();
            }
            if (Btn("Hepsini CLEANED yap", new Color(0.06f, 0.72f, 0.51f))) tester.MarkAllCleaned();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("4 · Grafikler", EditorStyles.boldLabel);

            string telemetryLabel = tester.FakeTelemetryRunning
                ? "Sahte telemetriyi DURDUR"
                : "Sahte telemetriyi BAŞLAT";
            Color telemetryColor = tester.FakeTelemetryRunning
                ? new Color(0.94f, 0.27f, 0.27f)
                : new Color(0.06f, 0.72f, 0.51f);
            if (Btn(telemetryLabel, telemetryColor)) tester.ToggleFakeTelemetry();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("5 · Tam Senaryo", EditorStyles.boldLabel);
            if (Btn("Baştan sona oynat (~15 sn)", new Color(0.18f, 0.83f, 0.75f)))
                tester.PlayFullScenario();
        }

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Referansları sahneden bul"))
            tester.ResolveReferences();
    }

    /// <summary>
    /// Renkli bir Inspector butonu çizer.
    /// </summary>
    /// <param name="label">Buton yazısı.</param>
    /// <param name="tint">Arka plan tonu.</param>
    /// <returns>Butona tıklandıysa true.</returns>
    private static bool Btn(string label, Color tint)
    {
        Color old = GUI.backgroundColor;
        GUI.backgroundColor = tint;
        bool clicked = GUILayout.Button(label, GUILayout.Height(26f));
        GUI.backgroundColor = old;
        return clicked;
    }
}
