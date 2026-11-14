using System;
using UnityEditor;
using UnityEngine;
namespace Severny.EditorTools
{
    public static class V14Checks
    {
        [MenuItem("Severny/v14/Проверить баланс, инвентарь и пробитие")]
        public static void Run()
        {
            foreach (var d in ItemDB.All)
            {
                Require(d.price > 0 && d.rarity >= 1 && d.rarity <= 5, "Баланс: " + d.id);
                if (d.type == ItemType.Weapon)
                {
                    var mag = ItemDB.Get(d.magId); var ammo = ItemDB.Get(ItemDB.DefaultAmmo(d.caliber));
                    Require(mag != null && mag.caliber == d.caliber && mag.capacity > 0, "Магазин: " + d.id);
                    Require(ammo != null && ammo.caliber == d.caliber, "Боеприпасы: " + d.id);
                }
            }
            var grid = new InventoryGrid("check", 1, 1);
            var partial = ItemInstance.Create("a545", 59); grid.AutoPlace(partial);
            var purchase = ItemInstance.Create("a545", 30);
            Require(!grid.TryAutoPlaceAll(purchase) && partial.count == 59 && purchase.count == 30, "Отмена покупки при полном схроне");
            var one = ItemInstance.Create("a545", 1);
            Require(grid.TryAutoPlaceAll(one) && partial.count == 60, "Покупка с объединением стопок");
            var root = new GameObject("v14_test_barrier");
            try
            {
                root.transform.position = new Vector3(0, -10000, 0);
                var box = root.AddComponent<BoxCollider>(); box.size = new Vector3(2, 2, 0.02f);
                var surface = root.AddComponent<BulletSurface>(); surface.thickness = 0.02f;
                Physics.SyncTransforms();
                var origin = root.transform.position - Vector3.forward;
                Require(box.Raycast(new Ray(origin, Vector3.forward), out var hit, 2f), "Попадание в тестовый коллайдер");
                Require(surface.TryCross(box, hit, Vector3.forward, 27, out _), "Тонкое дерево должно пробиваться");
                box.size = new Vector3(2, 2, 1); Physics.SyncTransforms();
                Require(box.Raycast(new Ray(origin, Vector3.forward), out hit, 3f), "Толстое дерево: попадание");
                Require(!surface.TryCross(box, hit, Vector3.forward, 27, out _), "Толстое дерево должно остановить пулю");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            Debug.Log("[Severny] v14: проверки инвентаря/баланса/тестовых коллайдеров прошли. Это не проверка карты, LAN, графики или FPS.");
        }
        public static void Batch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
