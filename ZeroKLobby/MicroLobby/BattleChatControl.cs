﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using LobbyClient;
using PlasmaShared;
using ZkData.UnitSyncLib;
using ZeroKLobby.Lines;
using ZkData;

namespace ZeroKLobby.MicroLobby
{
    class BattleChatControl : ChatControl
    {
        private bool finishLoad = false; //wait for element to initialize before manipulate them in OnResize()
        Image minimap;
        readonly PictureBox minimapBox;
        readonly ZeroKLobby.Controls.MinimapFuncBox minimapFuncBox;
        Size minimapSize;
        public static event EventHandler<EventArgs<IChatLine>> BattleLine = delegate { };


        public BattleChatControl() : base("Battle")
        {
            if (this.IsInDesignMode()) return;

            Program.TasClient.Said += TasClient_Said;
            Program.TasClient.BattleJoinSuccess += TasClientBattleJoinSuccess;
            Program.TasClient.BattleUserLeft += TasClient_BattleUserLeft;
            Program.TasClient.BattleUserJoined += TasClient_BattleUserJoined;
            Program.TasClient.BattleUserStatusChanged += TasClient_BattleUserStatusChanged;
            Program.TasClient.BattleClosed += (s, e) => Reset();
            Program.TasClient.ConnectionLost += (s, e) => Reset();
            Program.TasClient.BattleBotAdded += (s, e) => SortByTeam();
            Program.TasClient.BattleBotRemoved += (s, e) => SortByTeam();
            Program.TasClient.BattleBotUpdated += (s, e) => SortByTeam();
            Program.TasClient.BattleMapChanged += TasClient_BattleMapChanged;


            if (Program.TasClient.MyBattle != null) foreach (var user in Program.TasClient.MyBattle.Users) AddUser(user.Key);
            ChatLine += (s, e) => { if (Program.TasClient.IsLoggedIn) Program.TasClient.Say(SayPlace.Battle, null, e.Data, false); };
            playerBox.IsBattle = true;

            minimapFuncBox = new ZeroKLobby.Controls.MinimapFuncBox
            {
                Dock = DockStyle.Fill
            };


            minimapBox = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.CenterImage };
            minimapBox.Cursor = Cursors.Hand;
            minimapBox.Click +=
                (s, e) => { if (Program.TasClient.MyBattle != null) Program.MainWindow.navigationControl.Path = string.Format("{1}/Maps/DetailName?name={0}", Program.TasClient.MyBattle.MapName, GlobalConst.BaseSiteUrl); };

            // playerBoxSearchBarContainer.Controls.Add(battleFuncBox);
            playerListMapSplitContainer.Panel2.Controls.Add(minimapFuncBox);
            minimapFuncBox.mapPanel.Controls.Add(minimapBox);

            minimapFuncBox.Visible = false; //hide button before joining game 
            playerListMapSplitContainer.Panel2Collapsed = false; //show mappanel when in battleroom
            finishLoad = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (Program.TasClient != null) Program.TasClient.UnsubscribeEvents(this);
            base.Dispose(disposing);
        }

        public override void AddLine(IChatLine line)
        {
            base.AddLine(line);
            BattleLine(this, new EventArgs<IChatLine>(line));
        }

        protected override void OnLoad(EventArgs ea)
        {
            base.OnLoad(ea);
        }


        // todo: check if this is called when joining twice the same mission

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (finishLoad && minimapFuncBox.minimapSplitContainer1.Height > 1)
            {
                var splitContainer = minimapFuncBox.minimapSplitContainer1;

                var splitterDistance = Math.Min((int)23, minimapFuncBox.minimapSplitContainer1.Height);  //always show button fully

                // SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize.
                splitterDistance = Math.Min(splitterDistance, splitContainer.Width - splitContainer.Panel2MinSize + 1);
                splitterDistance = Math.Max(splitterDistance, splitContainer.Panel1MinSize - 1);
                minimapFuncBox.minimapSplitContainer1.SplitterDistance = splitterDistance;
                DrawMinimap();
            }
        }

        public override void Reset()
        {
            playerListMapSplitContainer.Panel2Collapsed = false;
            minimapFuncBox.Visible = true; //show button when joining game 
            base.Reset();
            minimapBox.Image = null;
            minimap = null;
            Program.ToolTip.Clear(minimapBox);
        }

        protected override void SortByTeam()
        {
            if (filtering || Program.TasClient.MyBattle == null) return;

            var newList = new List<PlayerListItem>();

            foreach (var us in PlayerListItems) newList.Add(us);


            var nonSpecs = PlayerListItems.Where(p => p.UserBattleStatus != null && !p.UserBattleStatus.IsSpectator);
            var existingTeams = nonSpecs.GroupBy(i => i.UserBattleStatus.AllyNumber).Select(team => team.Key).ToList();

            foreach (var bot in Program.TasClient.MyBattle.Bots.Values)
            {
                newList.Add(new PlayerListItem
                { BotBattleStatus = bot, SortCategory = bot.AllyNumber * 2 + 1 + (int)PlayerListItem.SortCats.Uncategorized, AllyTeam = bot.AllyNumber });
                existingTeams.Add(bot.AllyNumber);
            }

            // add section headers
            if (PlayerListItems.Any(i => i.UserBattleStatus != null && i.UserBattleStatus.IsSpectator)) newList.Add(new PlayerListItem { Button = "Spectators", SortCategory = (int)PlayerListItem.SortCats.SpectatorTitle, IsSpectatorsTitle = true, Height = 25 });

            var buttonTeams = existingTeams.Distinct();
            foreach (var team in buttonTeams)
            {
                int numPlayers = nonSpecs.Where(p => p.UserBattleStatus.AllyNumber == team).Count();
                int numBots = Program.TasClient.MyBattle.Bots.Values.Where(p => p.AllyNumber == team).Count();
                int numTotal = numPlayers + numBots;

                var allianceName = "Team " + (team + 1) + (numTotal > 3 ? $"  ({numTotal})" : "");
                if (Program.TasClient.MyBattle?.Mode != AutohostMode.None) allianceName = team == 0 ? $"Players ({numTotal})"  : "Bots";

                newList.Add(new PlayerListItem { Button = allianceName, SortCategory = team * 2 + (int)PlayerListItem.SortCats.Uncategorized, AllyTeam = team, Height = 25 });
            }

            newList = newList.OrderBy(x => x.GetSortingKey()).ToList();


            playerBox.BeginUpdate();
            int currentScroll = playerBox.TopIndex;

            playerBox.Items.Clear();
            foreach (var item in newList) playerBox.Items.Add(item);

            playerBox.TopIndex = currentScroll;
            playerBox.EndUpdate();
        }

        protected override void client_ChannelUserAdded(object sender, ChannelUserInfo e) { }

        protected override void client_ChannelUserRemoved(object sender, ChannelUserRemovedInfo e) { }

        void DrawMinimap()
        {
            try
            {
                if (minimap == null || Program.TasClient.MyBattle == null || this.IsInDesignMode()) return;
                var boxColors = new[]
                                {
                                    Color.Green, Color.Red, Color.Blue, Color.Cyan, Color.Yellow, Color.Magenta, Color.Gray, Color.Lime, Color.Maroon,
                                    Color.Navy, Color.Olive, Color.Purple, Color.Silver, Color.Teal, Color.White,
                                };
                var xScale = (double)minimapBox.Width / minimapSize.Width;
                // todo remove minimapSize and use minimap image directly when plasmaserver stuff fixed
                var yScale = (double)minimapBox.Height / minimapSize.Height;
                var scale = Math.Min(xScale, yScale);
                minimapBox.Image = minimap.GetResized((int)(scale * minimapSize.Width), (int)(scale * minimapSize.Height));
                minimapBox.Invalidate();
            }
            catch (Exception ex)
            {
                Trace.TraceError("Error updating minimap: {0}", ex);
            }
        }


        void RefreshBattleUser(string userName)
        {
            if (Program.TasClient.MyBattle == null) return;
            UserBattleStatus userBattleStatus;
            Program.TasClient.MyBattle.Users.TryGetValue(userName, out userBattleStatus);
            if (userBattleStatus != null)
            {
                AddUser(userName);
                SortByTeam();
            }
        }

        void SetMapImages(string mapName)
        {
            Program.ToolTip.SetMap(minimapBox, mapName);

            // todo add check before calling invoke invokes!!!
            Program.MetaData.GetMapAsync(mapName,
                                                       (map, minimap, heightmap, metalmap) => Program.MainWindow.InvokeFunc(() =>
                                                           {
                                                               if (Program.TasClient.MyBattle == null) return;
                                                               if (map != null && map.Name != Program.TasClient.MyBattle.MapName) return;
                                                               if (minimap == null || minimap.Length == 0)
                                                               {
                                                                   minimapBox.Image = null;
                                                                   this.minimap = null;
                                                               }
                                                               else
                                                               {
                                                                   this.minimap = Image.FromStream(new MemoryStream(minimap));
                                                                   minimapSize = map.Size;
                                                                   DrawMinimap();
                                                               }
                                                           }),
                                                       a => Program.MainWindow.InvokeFunc(() =>
                                                           {
                                                               minimapBox.Image = null;
                                                               minimap = null;
                                                           }));
        }


        void TasClientBattleJoinSuccess(object sender, Battle battle)
        {
            Reset();
            SetMapImages(battle.MapName);
            foreach (var user in Program.TasClient.MyBattle.Users.Values) AddUser(user.Name);
            base.AddLine(new SelfJoinedBattleLine(battle));
        }

        void TasClient_BattleMapChanged(object sender, OldNewPair<Battle> pair)
        {
            var tas = (TasClient)sender;
            if (tas.MyBattle == pair.New)
            {
                SetMapImages(pair.New.MapName);
            }
        }

        void TasClient_BattleUserJoined(object sender, BattleUserEventArgs e1)
        {
            var battleID = e1.BattleID;
            var tas = (TasClient)sender;
            if (tas.MyBattle != null && battleID == tas.MyBattle.BattleID)
            {
                var userName = e1.UserName;
                AddUser(userName);
                AddLine(new JoinLine(userName));
            }
        }

        void TasClient_BattleUserLeft(object sender, BattleUserEventArgs e)
        {
            var userName = e.UserName;
            if (userName == Program.Conf.LobbyPlayerName)
            {
                minimapFuncBox.Visible = false; //hide buttons when leaving game 
                playerListItems.Clear();
                playerBox.Items.Clear();
                filtering = false;
                playerSearchBox.Text = string.Empty;
            }
            if (PlayerListItems.Any(i => i.UserName == userName))
            {
                RemoveUser(userName);
                AddLine(new LeaveLine(userName));
            }
        }

        void TasClient_BattleUserStatusChanged(object sender, UserBattleStatus ubs)
        {
            RefreshBattleUser(ubs.Name);
        }


        void TasClient_Said(object sender, TasSayEventArgs e)
        {
            if (e.Place == SayPlace.Battle || e.Place == SayPlace.BattlePrivate)
            {
                if (e.Text.Contains(Program.Conf.LobbyPlayerName) && !Program.TasClient.MyUser.IsInGame && !e.IsEmote && e.UserName != GlobalConst.NightwatchName &&
                    !e.Text.StartsWith(string.Format("[{0}]", Program.TasClient.UserName)))
                {
                    Program.MainWindow.NotifyUser("chat/battle", string.Format("{0}: {1}", e.UserName, e.Text), false, true);
                }
                if (!e.IsEmote) AddLine(new SaidLine(e.UserName, e.Text, e.Time));
                else AddLine(new SaidExLine(e.UserName, e.Text, e.Time));
            }
        }

        protected override void PlayerBox_MouseClick(object sender, MouseEventArgs mea)
        {
            if (mea.Button == MouseButtons.Left)
            {
                if (this.playerBox.HoverItem != null)
                {
                    if (this.playerBox.HoverItem.IsSpectatorsTitle) ActionHandler.Spectate();
                    else if (this.playerBox.HoverItem.Button != null) ActionHandler.JoinAllyTeam(this.playerBox.HoverItem.AllyTeam.Value);
                }
            }

            if (mea.Button == MouseButtons.Right || !Program.Conf.LeftClickSelectsPlayer)
            {
                if (this.playerBox.HoverItem == null && mea.Button == MouseButtons.Right)
                { //right click on empty space
                    var cm = ContextMenus.GetPlayerContextMenu(Program.TasClient.MyUser, true);
                    Program.ToolTip.Visible = false;
                    try
                    {
                        cm.Show(playerBox, mea.Location);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError("Error displaying tooltip: {0}", ex);
                    }
                    finally
                    {
                        Program.ToolTip.Visible = true;
                    }
                    return;
                }
                //NOTE: code that display player's context menu on Left-mouse-click is in ChatControl.playerBox_MouseClick();
            }
            if (this.playerBox.HoverItem != null)
            {
                if (this.playerBox.HoverItem.BotBattleStatus != null)
                {
                    playerBox.SelectedItem = this.playerBox.HoverItem;
                    var cm = ContextMenus.GetBotContextMenu(this.playerBox.HoverItem.BotBattleStatus.Name);
                    Program.ToolTip.Visible = false;
                    try
                    {
                        cm.Show(playerBox, mea.Location);
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceError("Error displaying tooltip: {0}", ex);
                    }
                    finally
                    {
                        Program.ToolTip.Visible = true;
                    }
                    return;
                }
                /*
					if (playerBox.HoverItem.UserBattleStatus != null) {
						playerBox.SelectedItem = playerBox.HoverItem;
						var cm = ContextMenus.GetPlayerContextMenu(playerBox.HoverItem.User, true);
						Program.ToolTip.Visible = false;
						cm.Show(playerBox, mea.Location);
						Program.ToolTip.Visible = true;
					}*/
            }
            base.PlayerBox_MouseClick(sender, mea);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // BattleChatControl
            // 
            this.Name = "BattleChatControl";
            this.Size = new System.Drawing.Size(246, 242);
            this.ResumeLayout(false);

        }
    }
}

