using System.IO;
using UnityEditor;
using UnityEngine;
namespace Severny.EditorTools
{
    public static class V14Setup
    {
        const string SettingsPath = "Assets/SevernyOkrug/Resources/V14Settings.asset";
        [MenuItem("Severny/v14/Настройки баланса и графики")]
        public static void Settings()
        {
            EnsureSettings(); Selection.activeObject = AssetDatabase.LoadAssetAtPath<V14Settings>(SettingsPath);
        }
        public static void EnsureSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<V14Settings>(SettingsPath) == null)
            { AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<V14Settings>(), SettingsPath); AssetDatabase.SaveAssets(); }
        }
        [MenuItem("Severny/v14/Добавить пробиваемую поверхность к выбранным коллайдерам")]
        public static void AddSurfaces()
        {
            foreach (var go in Selection.gameObjects)
                if (go.GetComponent<Collider>() != null && go.GetComponent<BulletSurface>() == null) Undo.AddComponent<BulletSurface>(go);
        }
        [MenuItem("Severny/v14/Экспорт пакета после проверки игры")]
        public static void Export()
        {
            EnsureSettings();
            string path = EditorUtility.SaveFilePanel("Экспорт v14", "", "SevernyOkrug_v14", "unitypackage");
            if (string.IsNullOrEmpty(path)) return;
            AssetDatabase.ExportPackage("Assets/SevernyOkrug", path, ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies);
            Debug.Log("[Severny] Пакет экспортирован: " + Path.GetFullPath(path));
        }
    }
}
