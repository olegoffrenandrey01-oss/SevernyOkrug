using UnityEngine;
using UnityEngine.UI;
namespace Severny
{
    public partial class UIManager
    {
        Text[] _questText;
        Button[] _questAccept, _questClaim;
        UIScreen _questReturn;
        void BuildQuests()
        {
            var s = NewScreen(UIScreen.Quests); Background(s, 0.94f);
            var f = UIFactory.Center(s);
            ScreenHeader(f, "Главное меню / Задания", "Задания — F6");
            _questText = new Text[QuestSystem.All.Length];
            _questAccept = new Button[_questText.Length]; _questClaim = new Button[_questText.Length];
            for (int i = 0; i < _questText.Length; i++)
            {
                var d = QuestSystem.All[i]; float y = 180 + i * 230;
                var card = UIFactory.PanelBox(f, "Quest_" + d.id, 100, y, 1720, 210, 0.95f);
                _questText[i] = UIFactory.T(card, "", 30, 16, 1250, 180, UITheme.Body, 22, UITheme.Text0, TextAnchor.UpperLeft, "description");
                _questAccept[i] = UIFactory.Button(card, "ПРИНЯТЬ", 1350, 30, 320, 56, () => { Toast(QuestSystem.Accept(d) ? "Задание принято" : "Не удалось сохранить задание"); RefreshQuests(); });
                _questClaim[i] = UIFactory.Button(card, "ПОЛУЧИТЬ НАГРАДУ", 1350, 115, 320, 56, () => { Toast(QuestSystem.Claim(d) ? "Награда получена" : "Сдача доступна вне рейда; проверьте цель и сохранение"); RefreshQuests(); });
            }
            UIFactory.Button(f, "НАЗАД / ESC", 100, 940, 400, 60, CloseQuests);
            UIFactory.T(f, "В одиночной игре журнал ставит рейд на паузу. В LAN мир продолжает работать. Принятие и сдача — вне рейда. Прогресс сохраняется при завершении рейда.", 550, 945, 1250, 80, UITheme.Body, 18, UITheme.Text1, TextAnchor.UpperLeft, "help");
        }
        public void OpenQuests() { _questReturn = Current; RefreshQuests(); Show(UIScreen.Quests); }
        void CloseQuests() { Show(InRaid && RaidManager.I.Running ? UIScreen.Hud : _questReturn == UIScreen.Base ? UIScreen.Base : UIScreen.MainMenu); }
        void RefreshQuests()
        {
            var p = SaveSystem.Current;
            for (int i = 0; i < _questText.Length; i++)
            {
                var d = QuestSystem.All[i]; var s = QuestSystem.State(p, d.id);
                _questText[i].text = d.title + "\n" + d.description + "\nПрогресс: " + QuestSystem.Progress(p, d) + "/" + d.target +
                    "   Награда: " + d.money + " ₽, " + d.xp + " опыта\n" + (s.claimed ? "Завершено" : s.active ? "Принято" : "Не принято");
                _questAccept[i].interactable = !s.active && !s.claimed && !InRaid;
                _questClaim[i].interactable = s.active && !s.claimed && QuestSystem.Progress(p, d) >= d.target && !InRaid;
            }
        }
    }
}
