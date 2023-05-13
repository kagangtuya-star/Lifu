using System;
using System.Collections.Generic;
using System.Diagnostics;
using Num = System.Numerics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using Dalamud.Game;
using Dalamud.Plugin;
using System.Numerics;
using Dalamud.Logging;
using Dalamud.Hooking;
using LiFu;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui.Toast;
using System.ComponentModel;
using Lumina.Excel.GeneratedSheets;
using ImGuiNET;
using ClickLib.Clicks;
using FFXIVClientStructs.Attributes;
using FFXIVClientStructs.FFXIV.Client.Game;
using System.Reflection.Emit;
using static System.Net.WebRequestMethods;
using Lumina.Excel;

namespace Lifu
{
    public unsafe class Lifu : IDalamudPlugin
    {
        public string Name => "Lifu";
        public static Lifu Plugin { get; private set; }
        public static Configuration Config { get; set; }
        private Configuration config;
        private DalamudPluginInterface pluginInterface;

        private AccessGameObjDelegate? AccessGameObject;
        private delegate void AccessGameObjDelegate(IntPtr g_ControlSystem_TargetSystem, IntPtr targte, char p3);

        private Hook<TakenQeustHook> takenQeustHook;
        private delegate IntPtr TakenQeustHook(long a1, long questId);
        private IntPtr TakenQeustParam1;

        private Hook<RequestHook> requestHook;
        private delegate IntPtr RequestHook(long a, InventoryItem* b, int c, Int16 d, byte e);
        public IntPtr InvManager;
		List<Leve> LeveList;
		List<Leve> LeveList1;
		public InventoryItem* TargetInvSlot = (InventoryItem*) IntPtr.Zero;

        private delegate IntPtr LeveHook(IntPtr a);
        private Hook<LeveHook> leveHook;
        private static RaptureAtkUnitManager* raptureAtkUnitManager;
        ExcelSheet<Leve> Levesheet;
        ExcelSheet<Item> Itemsheet;
		int LeveQuestId;
        string LeveQuestName;
        int LeveItemId;
        int LeveItemMagic;

        string LeveTakenGui;
        string LeveNpc1;
        string LeveNpc2;

        private DateTime NextClick;
        private DateTime NextTarget;

        bool Debug;
        bool Dirty;

        public Lifu(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
            DalamudApi.Initialize(this, pluginInterface);
            this.config = ((Configuration) this.pluginInterface.GetPluginConfig()) ?? new Configuration();
            this.config.Initialize();

            AccessGameObject = Marshal.GetDelegateForFunctionPointer<AccessGameObjDelegate>(DalamudApi.SigScanner.ScanText("E9 ?? ?? ?? ?? 48 8B 01 FF 50 08"));

            TakenQeustParam1 = DalamudApi.SigScanner.GetStaticAddressFromSig("48 89 05 ?? ?? ?? ?? 8B 44 24 70");
            InvManager = (IntPtr) InventoryManager.Instance();

            this.NextClick = DateTime.Now;
            this.NextTarget = DateTime.Now;

            takenQeustHook ??= Hook<TakenQeustHook>.FromAddress(DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? 0F B6 D8 EB ?? 48 8B 01"), TakenQeustDetour);
            takenQeustHook.Enable();
            requestHook ??= Hook<RequestHook>.FromAddress(DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8B CE E8 ?? ?? ?? ?? 48 8B 5C 24 40 4C 8B 74 24 48"), RequestDetour);
            requestHook.Enable();
            leveHook ??= Hook<LeveHook>.FromAddress(DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8D BB ?? ?? ?? ?? 33 D2 8D 4E 10"), LeveDetour);
            leveHook.Enable();

            Enabled = false;
            Debug = false;
            Dirty = false;

            raptureAtkUnitManager = AtkStage.GetSingleton()->RaptureAtkUnitManager;
            DalamudApi.Framework.Update += Update;
            pluginInterface.UiBuilder.Draw += Draw;
            pluginInterface.UiBuilder.OpenConfigUi += ToggleUI;
			 Levesheet = DalamudApi.DataManager.GetExcelSheet<Leve>();
		    Itemsheet = DalamudApi.DataManager.GetExcelSheet<Item>();
			 LeveList = new List<Leve>();
			 LeveList1 = new List<Leve>();
			foreach (var item in Levesheet)
			{
				LeveList.Add(item);

			}
			SetLeve();
        }

        #region IDisposable Support
        public void Dispose()
        {
            takenQeustHook.Disable();
            requestHook.Disable();
            leveHook.Disable();
            DalamudApi.Framework.Update -= Update;
            pluginInterface.UiBuilder.Draw -= Draw;
            pluginInterface.UiBuilder.OpenConfigUi -= ToggleUI;
            DalamudApi.Dispose();
            GC.SuppressFinalize(this);
        }
        #endregion

        private IntPtr RequestDetour(long a, InventoryItem* b, int c, short d, byte e)
        {
            if (Debug)
            {
                // PluginLog.Log($"[RequestHook] ReqParam1 -> {InvManager:X}");
                // PluginLog.Log($"[RequestHook] ItemSlot -> {:X}");
                PluginLog.Log($"[RequestHook] {a:X} {(IntPtr) b:X} {c} {d} {e:X}");
                PluginLog.Log($"[RequestHook] Magic={c}");
            }

            if (Dirty)
            {
                Print($"[RequestHook] Magic={c} 设置完成。");
                LeveItemMagic = c;
                config.Save();
                Dirty = false;
            }
            return requestHook.Original(a, b, c, d, e);
        }
        private IntPtr TakenQeustDetour(long a1, long a2) => takenQeustHook.Original(a1, a2);
        private IntPtr LeveDetour(IntPtr a)
        {
            return leveHook.Original(a);
        }

        private void SetLeve()
        {
            LeveQuestId = config.LeveQuestId;
            LeveQuestName = DalamudApi.DataManager.GetExcelSheet<Leve>().GetRow((uint)LeveQuestId).Name;
            var DataId = DalamudApi.DataManager.GetExcelSheet<Leve>().GetRow((uint)LeveQuestId).DataId;
            LeveItemId = DalamudApi.DataManager.GetExcelSheet<CraftLeve>().GetRow((uint)DataId).UnkData3[0].Item;
            var ItemName = DalamudApi.DataManager.GetExcelSheet<Item>().GetRow((uint)LeveItemId).Name;
            LeveItemMagic = config.LeveItemMagic;
            LeveNpc1 = config.LeveNpc1;
            LeveNpc2 = config.LeveNpc2;
            LeveTakenGui = $"将{ItemName}提交给{LeveNpc2}";
        }

        public static bool IsAddonReady(AtkUnitBase* addon)
        {
            return addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded;
        }

        private bool Enabled;
        Random rd = new Random();

        private bool await = false;

        private void Update(Framework framework)
        {
            var isMainMenu = !DalamudApi.Condition.Any();
            if (isMainMenu) return;

            if (DalamudApi.Condition[ConditionFlag.OccupiedInQuestEvent] || DalamudApi.Condition[ConditionFlag.OccupiedInEvent])
            {
                if (Enabled)
                {
                    var now = DateTime.Now;
                    if (this.NextClick > now)
                    {
                        return;
                    }

                    this.NextClick = DateTime.Now.AddMilliseconds(Math.Min(100, rd.Next(2) * 100));
                    TickTalk();
                    SelectString("有什么事？", 3);
                    SelectIconString(LeveQuestName);
                    SubmitQuestItem(LeveItemMagic);
                    SelectYes("确定要交易优质道具吗？");
                    TickQuestComplete();

                    NextTarget = DateTime.Now.AddMilliseconds(config.TargetDelay);
                    await = false;
                }
            } else
            {
                if (Enabled && config.AutoTarget)
                {
                    if ((IntPtr)TargetInvSlot != IntPtr.Zero && TargetInvSlot->ItemID == 0)
                    {
                        FindItem(); // We're trying to find the item again to hand over un-stackable shit
                        if ((IntPtr)TargetInvSlot != IntPtr.Zero && TargetInvSlot->ItemID == 0)
                        {
                            Enabled = false;
                            TargetInvSlot = (InventoryItem*) IntPtr.Zero;
                            PrintError("背包内没有理符要求的物品!");
                            return;
                        }
                    }

                    if (!await && DateTime.Now > NextTarget)
                    {
                        targetByName(!IsLeveExists((ushort) LeveQuestId) ? config.LeveNpc1 : config.LeveNpc2);
                        await = true;
                    }
                }
            }
        }

        [Command("/lifu")]
        [HelpMessage("简化理符 [toggle|a|b|config]\n第一次需要手动交下任务")]
        public void LifuCommand(string command, string args)
        {
            string[] array = args.Split(new char[] { ' ' });
            string subCommand = array[0];
            switch (subCommand)
            {
                case "a":
                    targetByName(LeveNpc1);
                    break;
                case "b":
                    targetByName(LeveNpc2);
                    break;
                case "config":
                    ToggleUI();
                    break;
                case "tc":
                    TickQuestComplete();
                    break;
                case "tt":
                    TickTalk();
                    break;
                case "yes":
                    SelectYes("确定要交易优质道具吗？");
                    break;
                case "esc":
                    Task.Run(() => {
                        Thread.Sleep(10);
                        MouseDo.SendKeycode((uint)VirtualKey.ESCAPE);
                    });
                    break;
                case "ceshi":
                    break;
                case "submit":
                    SubmitQuestItem(LeveItemMagic);
                    break;
                case "toggle":
                    Toggle();
                    break;
                default:
                    break;
            }
        }

        public void Toggle()
        {
            if (!Enabled)
            {
                TargetInvSlot = (InventoryItem*)IntPtr.Zero;
                FindItem();
                if ((IntPtr)TargetInvSlot == IntPtr.Zero)
                {
                    PrintError("背包内没有理符要求的物品!");
                    PrintError("如果是武器, 请放到背包, 不要放在兵装库!");
                    return;
                }
            }

            Enabled = !Enabled;
            DalamudApi.Toasts.ShowQuest("理符辅助 " + (Enabled ? "开启" : "关闭"),
            new QuestToastOptions() { PlaySound = true, DisplayCheckmark = true });
        }

        private bool SettingsVisible = false;

        private void ToggleUI()
        {
            SettingsVisible = !SettingsVisible;
        }
        int idx = 0;
		string lifuName = "";
		int LeveID = 0;

		public void Draw()
        {
            if (!SettingsVisible)
            {
                return;
            }

            if (ImGui.Begin("理符设置", ref this.SettingsVisible, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
            {
                ImGui.Text(LeveQuestName);

                if (ImGui.Button(Enabled ? "禁用" : "启用"))
                {
                    Toggle();
                }

				string tmp = "";


				if (ImGui.BeginCombo("名字", "理符任务选择", ImGuiComboFlags.None))
                {
                    
					if (ImGui.InputTextWithHint("过滤", "Filter...", ref tmp, 255))
                    {
						lifuName = tmp;
                    }
                    if (lifuName != " ")
                    {
                        LeveList1 = LeveList.Where(i => i.Name.ToString().Contains(lifuName)).ToList();

                    }
                    else  LeveList1 = LeveList;

                    for (int i = 0; i < LeveList1.Count; i++)
                    {
                        if (LeveList1[i].Name!="")
                        {
							if (ImGui.Selectable(LeveList1[i].Name, true))
							{
								LeveID = i;
                                lifuName = " ";
							}
						}
                       
                    }
						ImGui.EndCombo();
				}
                if (LeveList1.Count> LeveID)
                {
					var abc = LeveList.Where(i => i == LeveList1[LeveID]).FirstOrDefault();
                    idx = (int)abc.RowId;
                    if (idx>0) config.LeveQuestId =idx;
                    config.Save();
				}

				//ImGui.Text(lifuName);
				var _LeveQuestId = config.LeveQuestId;
                if (ImGui.InputInt("理符任务ID", ref _LeveQuestId))
                {
                    config.LeveQuestId = _LeveQuestId;
                    config.Save();
                    SetLeve();
                    Dirty = true;
                }
                var _LeveItemMagic = config.LeveItemMagic;
                if (ImGui.InputInt("递交物品魔数", ref _LeveItemMagic, 1, 1, Debug ? 0 : ImGuiInputTextFlags.ReadOnly))
                {
                    config.LeveItemMagic = _LeveItemMagic;
                    config.Save();
                    SetLeve();
                }

                bool _autoTarget = config.AutoTarget;
                if (ImGui.Checkbox("自动选择NPC", ref _autoTarget))
                {
                    config.AutoTarget = _autoTarget;
                    config.Save();
                }

                int _targetDelay = config.TargetDelay;
                if (ImGui.InputInt("自动选择NPC延时(毫秒)", ref _targetDelay))
                {
                    config.TargetDelay = _targetDelay;
                    config.Save();
                }

                var _npc1 = config.LeveNpc1;
                if (ImGui.InputText("接任务NPC", ref _npc1, 16))
                {
                    config.LeveNpc1 = _npc1;
                    config.Save();
                    SetLeve();
                }
                ImGui.SameLine();
                if (ImGui.Button("选中"))
                {
                    targetByName(config.LeveNpc1);
                }

                var _npc2 = config.LeveNpc2;
                if (ImGui.InputText("交任务NPC", ref _npc2, 16))
                {
                    config.LeveNpc2 = _npc2;
                    config.Save();
                    SetLeve();
                }
                ImGui.SameLine();
                if (ImGui.Button("选中2"))
                {
                    targetByName(config.LeveNpc2);
                }

                ImGui.Text("如果不想用插件的自动选择NPC，请使用SND之类的插件手动选择。");
                ImGui.Text("请不要手动修改物品魔数！修改理服任务后会在下一次手动递交后获取。");
                ImGui.Text("请不要轻易勾选下面的按钮，除非你知道你在干什么");

                ImGui.Checkbox("调试", ref Debug);
                if (ImGui.Button("从下次提交获取参数"))
                {
                    Dirty = true;
                }
                ImGui.SameLine();
                ImGui.Text(Dirty ? "：是" : "：否");
            }
		}

        void TickTalk()
        {
            var addon = DalamudApi.GameGui.GetAddonByName("Talk", 1);
            if (addon == IntPtr.Zero) return;
            var talkAddon = (AtkUnitBase*)addon;
            if (!talkAddon->IsVisible) return;

            var questAddon = (AtkUnitBase*)addon;
            var textComponent = (AtkComponentNode*)questAddon->UldManager.NodeList[20];
            var a = (AtkTextNode*)textComponent;

            if (LeveNpc1 == Marshal.PtrToStringUTF8((IntPtr)a->NodeText.StringPtr) && !IsLeveExists((ushort) LeveQuestId))
            {
                var b = Marshal.ReadInt64(TakenQeustParam1);
                if (b > 0) takenQeustHook.Original(b, LeveQuestId);
            }
            else
            {   //跳对话
                ClickTalk.Using(addon).Click();
                //clickManager.SendClick(addon, ClickManager.EventType.MOUSE_CLICK, 0, ((AddonTalk*)talkAddon)->AtkEventListenerUnk.AtkStage);
            }
        }
        bool IsLeveExists(ushort leveId)
        {
            return QuestManager.Instance()->GetLeveQuestById(leveId) != null;
        }

        bool takenLeve(string text)
        {
            var dataHolder = ((UIModule*)DalamudApi.GameGui.GetUIModule())->GetRaptureAtkModule()->AtkModule.AtkArrayDataHolder;
            var dataHolderContent = dataHolder.StringArrays[22];
            var size = dataHolderContent->AtkArrayData.Size;
            var array = dataHolderContent->StringArray;
            for (var i = 0; i < size; i++)
            {
                if (array[i] == null) continue;
                var seString = ReadSeString(array[i]).TextValue;
                if (seString.Contains(text)) return true;
            }
            return false;
        }
        void TickQuestComplete()
        {
            var addon = DalamudApi.GameGui.GetAddonByName("JournalResult", 1);
            if (addon == IntPtr.Zero) return;
            var questAddon = (AtkUnitBase*)addon;
            if (questAddon->UldManager.NodeListCount <= 4) return;
            var buttonNode = (AtkComponentNode*)questAddon->UldManager.NodeList[4];
            if (buttonNode->Component->UldManager.NodeListCount <= 2) return;
            var textComponent = (AtkTextNode*)buttonNode->Component->UldManager.NodeList[2];
            if ("完成" != Marshal.PtrToStringUTF8((IntPtr)textComponent->NodeText.StringPtr)) return;
            if (!((AddonJournalResult*)addon)->CompleteButton->IsEnabled) return;
            ClickJournalResult.Using(addon).Complete();
            //clickManager.SendClickThrottled(addon, EventType.CHANGE, 1, ((AddonJournalResult*)addon)->CompleteButton->AtkComponentBase.OwnerNode);
        }
        void SubmitQuestItem(int itemSId)
        {
            var addon = DalamudApi.GameGui.GetAddonByName("Request", 1);
            if (addon == IntPtr.Zero) return;
            var selectStrAddon = (AtkUnitBase*)addon;
            if (!selectStrAddon->IsVisible) return;
            if (selectStrAddon->UldManager.NodeListCount <= 3) return;
            var HighlighIcon = (AtkComponentNode*)selectStrAddon->UldManager.NodeList[16];
            var Ready = !HighlighIcon->AtkResNode.IsVisible;
            var focusedAddon = GetFocusedAddon();
            var addonName = focusedAddon != null ? Marshal.PtrToStringAnsi((IntPtr)focusedAddon->Name) : string.Empty;
            
            if (!Ready == true && InvManager != 0)
            {
                //准备到提交框
                if (Dirty) return;
                if ((IntPtr) TargetInvSlot == IntPtr.Zero || TargetInvSlot->ItemID == 0)
                {
                    FindItem(); // Just incase
                }
                else
                {
                    requestHook.Original(InvManager, TargetInvSlot, itemSId, 0, 1);
                }
            }

            if (Ready)
            {
                var questAddon = (AtkUnitBase*)addon;
                var buttonNode = (AtkComponentNode*)questAddon->UldManager.NodeList[4];
                if (buttonNode->Component->UldManager.NodeListCount <= 2) return;
                var textComponent = (AtkTextNode*)buttonNode->Component->UldManager.NodeList[2];
                var abc = Marshal.PtrToStringUTF8((IntPtr)textComponent->NodeText.StringPtr);

                if ("递交" != Marshal.PtrToStringUTF8((IntPtr)textComponent->NodeText.StringPtr)) return;
                var eventListener = (AtkEventListener*)addon;
                var receiveEventAddress = new IntPtr(eventListener->vfunc[2]);
                if (addonName == "Request")
                {
                    //点击提交
                    ClickRequest.Using(addon).HandOver();
                    //clickManager.SendClickThrottled(addon, EventType.CHANGE, 0, buttonNode);
                }
                //else
                //{//点击前先焦点
                //    clickManager.SendClickThrottled(addon, EventType.FOCUS_MAX, 2, buttonNode);
                //}
            }
        }

        void FindItem()
        {
            for (int i = 0; i < 4; ++i) // Inventory1-4
            {
                InventoryContainer* container = InventoryManager.Instance()->GetInventoryContainer((InventoryType)i);
                for (int j = 0; j < container->Size; ++j)
                {
                    InventoryItem* item = container->GetInventorySlot(j);
                    if (item is not null && item->ItemID == LeveItemId) // 理符所需的物品ID
                    {
                        TargetInvSlot = item;
                        break;
                    }
                }
            }
        }

        void SelectString(string title, int index)
        {
            var addon = DalamudApi.GameGui.GetAddonByName("SelectString", 1);
            if (addon == IntPtr.Zero) return;
            var selectStrAddon = (AtkUnitBase*)addon;
            if (!selectStrAddon->IsVisible) return;
            if (selectStrAddon->UldManager.NodeListCount <= 3) return;
            var a = (AtkComponentNode*)selectStrAddon->UldManager.NodeList[2];
            var txt = (AtkTextNode*)selectStrAddon->UldManager.NodeList[3];
            if (title == Marshal.PtrToStringUTF8((IntPtr)txt->NodeText.StringPtr))
            {
                ClickSelectString.Using(addon).SelectItem((ushort)index);
                //clickManager.SelectStringClick(addon, index);
            }
        }
        void SelectIconString(string title)
        {
            var addon = DalamudApi.GameGui.GetAddonByName("SelectIconString", 1);
            if (addon == IntPtr.Zero) return;
            var selectStrAddon = (AtkUnitBase*)addon;
            if (!selectStrAddon->IsVisible) return;
            if (selectStrAddon->UldManager.NodeListCount <= 3) return;
            var a = ((AtkComponentNode*)selectStrAddon->UldManager.NodeList[2])->Component->UldManager;
            var size = a.NodeListCount;
            if (size < 12) return;
            for (var i = 1; i <= 8; i++)
            {
                var d = ((AtkComponentNode*)a.NodeList[i])->Component->UldManager;
                if (d.NodeListCount < 5) return;
                var txt = (AtkTextNode*)d.NodeList[4];
                if (title == Marshal.PtrToStringUTF8((IntPtr)txt->NodeText.StringPtr))
                {
                    if (!IsAddonReady(selectStrAddon))
                    {
                        return;
                    }
                    ClickSelectIconString.Using(addon).SelectItem((ushort)(i - 1));
                    //clickManager.SelectStringClick(addon, i-1);
                    return;
                }
            }
        }
        void SelectYes(string title)
        {
            var addon = DalamudApi.GameGui.GetAddonByName("SelectYesno", 1);
            if (addon == IntPtr.Zero) return;
            var selectStrAddon = (AtkUnitBase*)addon;
            if (!selectStrAddon->IsVisible) return;
            if (selectStrAddon->UldManager.NodeListCount <= 6) return;
            var a = (AtkComponentNode*)selectStrAddon->UldManager.NodeList[11];
            var txt = (AtkTextNode*)selectStrAddon->UldManager.NodeList[15];
            if (title != Marshal.PtrToStringUTF8((IntPtr)txt->NodeText.StringPtr)) return;
            if (a->Component->UldManager.NodeListCount <= 2) return;
            var b = (AtkTextNode*)a->Component->UldManager.NodeList[2];
            if ("确定" != Marshal.PtrToStringUTF8((IntPtr)b->NodeText.StringPtr)) return;
            ClickSelectYesNo.Using(addon).Yes();
            //clickManager.SendClick(addon, EventType.CHANGE, 0, ((AddonSelectYesno*)addon)->YesButton->AtkComponentBase.OwnerNode);
        }
        void targetByName(string name)
        {
            Task.Run(() => {
                GameObject Actor = DalamudApi.ObjectTable.Where(i => i.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc && i.Name.ToString() == name).FirstOrDefault();
                if (Actor != null)
                {
                    AccessGameObject(DalamudApi.TargetManager.Address, Actor.Address, (char)0);
                }
            });
        }
        public static AtkUnitBase* GetFocusedAddon()
        {
            var units = raptureAtkUnitManager->AtkUnitManager.FocusedUnitsList;
            var count = units.Count;
            return count == 0 ? null : (&units.AtkUnitEntries)[count - 1];
        }
        public SeString ReadSeString(byte* ptr)
        {
            var offset = 0;
            while (true)
            {
                var b = *(ptr + offset);
                if (b == 0) break;
                offset += 1;
            }
            var bytes = new byte[offset];
            Marshal.Copy(new IntPtr(ptr), bytes, 0, offset);
            return SeString.Parse(bytes);
        }
        public static void Print(string message) => DalamudApi.ChatGui.Print(message);
        public static void PrintEcho(string message) => DalamudApi.ChatGui.Print($"[Lifu] {message}");
        public static void PrintError(string message) => DalamudApi.ChatGui.PrintError($"[Lifu] {message}");

    }
}
