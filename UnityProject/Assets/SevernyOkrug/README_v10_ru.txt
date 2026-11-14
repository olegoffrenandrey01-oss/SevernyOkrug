СЕВЕРНЫЙ ОКРУГ — v10: КАРТА ГОРОДА ДЛЯ UNITY (экспорт из Blender)

ЧТО ЭТО
Пакет кладёт в Assets/SevernyOkrug/Map экспорт города из
SEVERNY_OKRUG_LA_cars_signs_detailed.blend (коллекция EXPORT, имена не менялись):
  SEV_Buildings_buildings.fbx  — 192 здания SM_Bld_*
  SEV_Interiors_interiors.fbx  — 140 интерьеров SM_Bld_*_Int
  SEV_Roads_roads.fbx, SEV_Ground_ground.fbx — дороги и земля
  SEV_Props_props.fbx          — пропсы SM_Props_*
  SEV_Trunks_trees.fbx, SEV_Foliage_foliage.fbx — стволы и листва
  SEV_Cars_cars.fbx (763 машины), SEV_Signs_signs.fbx (121 вывеска)
  SEV_Backdrop_backdrop.fbx    — дальний фон (без коллайдеров)
  Textures/, severny_map_manifest.json, severny_materials.json
Всего 7,1 млн треугольников. Плюс Editor/SevernyMapImportV10.cs — правильные
настройки импорта FBX и текстур (внешние материалы, normal map).

УСТАНОВКА (поверх v09.1)
1. Assets > Import Package > Custom Package… > SevernyOkrug_v10_map.unitypackage >
   Import (всё). Импорт FBX и текстур займёт несколько минут.
2. Severny > 3 Place Map — город встанет в сцену Raid_Severny
   (материалы, коллайдеры, границы игровой зоны).
3. Severny > 4 Bake NavMesh — 1–3 минуты.
4. По желанию: Severny > 5 Validate Map (предупреждение «> 6M triangles» ожидаемо).
5. Play в Severny_Main > рейд «Северный округ».

ЕСЛИ ЧТО-ТО НЕ ТАК
- Город серый/без текстур после шага 2: Severny > 3a Map: Fix Import Settings,
  затем снова Severny > 3 и Severny > 4.
- Детализация v09 (SevernyDress) привяжется к зданиям сама; в Console будет
  «выравнивание по N зданиям … ошибка X м» (должно быть меньше 1 м).

Код игры и сеть не менялись.
