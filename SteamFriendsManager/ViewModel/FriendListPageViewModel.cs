﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MahApps.Metro.Controls.Dialogs;
using SteamFriendsManager.Service;
using SteamFriendsManager.Utility;
using SteamKit2;

namespace SteamFriendsManager.ViewModel
{
    public class FriendListPageViewModel : ViewModelBase
    {
        private RelayCommand _addFriend;
        private RelayCommand _changePersonaName;
        private RelayCommand<IList> _removeFriend;
        private string _searchText;
        private RelayCommand<IList> _sendChatMessage;
        private RelayCommand _switchAccount;
        private RelayCommand<EPersonaState> _switchPersonaState;
        private readonly SteamClientService _steamClientService;

        public FriendListPageViewModel(SteamClientService steamClientService)
        {
            _steamClientService = steamClientService;

            Task.Delay(2000).ContinueWith(task => { _steamClientService.SetPersonaStateAsync(EPersonaState.Online); });

            MessengerInstance.Register<PersonaNameChangedMessage>(this,
                msg => { RaisePropertyChanged(() => PersonaName); });

            MessengerInstance.Register<PersonaStateChangedMessage>(this,
                msg => RaisePropertyChanged(() => PersonaState));
        }

        public IEnumerable<SteamClientService.Friend> Friends
        {
            get { return _steamClientService.Friends; }
        }

        public string PersonaName
        {
            get { return _steamClientService.PersonaName; }
        }

        public string PersonaState
        {
            get
            {
                var state = _steamClientService.PersonaState;
                switch (state)
                {
                    case EPersonaState.Offline:
                        return "离线";

                    case EPersonaState.Online:
                        return "在线";

                    case EPersonaState.Busy:
                        return "忙碌";

                    case EPersonaState.Away:
                        return "离开";

                    case EPersonaState.Snooze:
                        return "打盹";

                    case EPersonaState.LookingToTrade:
                        return "想交易";

                    case EPersonaState.LookingToPlay:
                        return "想玩游戏";

                    default:
                        return state.ToString();
                }
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (_searchText == value)
                    return;

                _searchText = value;
                RaisePropertyChanged(() => SearchText);

                if (!string.IsNullOrEmpty(_searchText))
                {
                    foreach (var friend in _steamClientService.Friends)
                    {
                        friend.Show = friend.PersonaName.ToLower().Contains(_searchText.ToLower());
                    }
                }
                else
                {
                    foreach (var friend in _steamClientService.Friends)
                    {
                        friend.Show = true;
                    }
                }
            }
        }

        public RelayCommand SwitchAccount
        {
            get
            {
                return _switchAccount ?? (_switchAccount = new RelayCommand(() =>
                {
                    MessengerInstance.Send(new ClearPageHistoryOnNextTryLoginMessage());
                    MessengerInstance.Send(new LogoutOnNextTryLoginMessage());
                    MessengerInstance.Send(new SwitchPageMessage(SwitchPageMessage.Page.Login));
                }));
            }
        }

        public RelayCommand ChangePersonaName
        {
            get
            {
                return _changePersonaName ?? (_changePersonaName = new RelayCommand(() =>
                {
                    MessengerInstance.Send(new ShowInputDialogMessage("修改昵称", "请输入新昵称：", PersonaName, async s =>
                    {
                        if (s == null || s == PersonaName)
                            return;

                        if (string.IsNullOrWhiteSpace(s))
                        {
                            MessengerInstance.Send(new ShowMessageDialogMessage("修改失败", "你不能使用空的昵称。"));
                            return;
                        }

                        try
                        {
                            await _steamClientService.SetPersonaNameAsync(s);
                        }
                        catch (TimeoutException)
                        {
                            MessengerInstance.Send(new ShowMessageDialogMessage("昵称修改失败", "连接超时，请重试。"));
                        }
                    }));
                }));
            }
        }

        public RelayCommand<EPersonaState> SwitchPersonaState
        {
            get
            {
                return _switchPersonaState ?? (_switchPersonaState = new RelayCommand<EPersonaState>(async state =>
                {
                    try
                    {
                        await _steamClientService.SetPersonaStateAsync(state);
                    }
                    catch (TimeoutException)
                    {
                        MessengerInstance.Send(new ShowMessageDialogMessage("切换状态失败", "连接超时，请重试。"));
                    }
                }));
            }
        }

        public RelayCommand<IList> SendChatMessage
        {
            get
            {
                return _sendChatMessage ?? (_sendChatMessage = new RelayCommand<IList>(friends =>
                {
                    if (friends == null || friends.Count == 0)
                        return;

                    if (friends.Count == 1)
                    {
                        var friend = friends[0] as SteamClientService.Friend;
                        if (friend == null)
                            return;

                        var uri = new Uri("steam:");
                        if (uri.CheckSchemeExistance())
                        {
                            Process.Start(uri.GetSchemeExecutable(),
                                string.Format("steam://friends/message/{0}", friend.SteamId.ConvertToUInt64()));
                        }
                        else
                        {
                            MessengerInstance.Send(new ShowInputDialogMessage("发送消息", "请输入内容：",
                                s =>
                                {
                                    _steamClientService.SendChatMessageAsync(friend.SteamId, EChatEntryType.ChatMsg, s);
                                }));
                        }
                    }
                    else
                    {
                        MessengerInstance.Send(new ShowInputDialogMessage("群发消息", "请输入内容：", async s =>
                        {
                            if (string.IsNullOrEmpty(s))
                                return;

                            if (await Task<bool>.Factory.StartNew(() =>
                            {
                                var sendTasks = from friend in friends.OfType<SteamClientService.Friend>()
                                    select _steamClientService.SendChatMessageAsync(friend.SteamId,
                                        EChatEntryType.ChatMsg, string.Format("{0} ♥", s));
                                try
                                {
                                    Task.WaitAll(sendTasks.ToArray());
                                    return true;
                                }
                                catch (AggregateException)
                                {
                                    return false;
                                }
                            }))
                            {
                                MessengerInstance.Send(new ShowMessageDialogMessage("群发成功", "所有消息都已成功送达！"));
                            }
                            else
                            {
                                MessengerInstance.Send(new ShowMessageDialogMessage("群发失败", "部分消息发送超时。"));
                            }
                        }));
                    }
                }));
            }
        }

        public RelayCommand AddFriend
        {
            get
            {
                return _addFriend ?? (_addFriend = new RelayCommand(() =>
                {
                    MessengerInstance.Send(new ShowInputDialogMessage("添加好友", @"请输入你要添加好友ID，当前支持以下几种格式：
* 对方用户名
* STEAM_0:0:19874118
* 76561198000013964
* http://steamcommunity.com/profiles/76561198000013964",
                        async s =>
                        {
                            if (s == null)
                                return;

                            if (string.IsNullOrWhiteSpace(s))
                                MessengerInstance.Send(new ShowMessageDialogMessage("添加失败", "输入无效"));

                            try
                            {
                                SteamFriends.FriendAddedCallback result;
                                do
                                {
                                    if (Regex.IsMatch(s, @"^7656\d{13}$"))
                                    {
                                        UInt64 steamId64;
                                        UInt64.TryParse(s, out steamId64);
                                        var steamId = new SteamID();
                                        steamId.SetFromUInt64(steamId64);
                                        result = await _steamClientService.AddFriendAsync(steamId64);
                                        break;
                                    }

                                    var match = Regex.Match(s,
                                        @"^http(?:s)?://steamcommunity.com/profiles/(7656\d{13})$");
                                    if (match.Success)
                                    {
                                        UInt64 steamId64;
                                        UInt64.TryParse(match.Groups[1].Value, out steamId64);
                                        var steamId = new SteamID();
                                        steamId.SetFromUInt64(steamId64);
                                        result = await _steamClientService.AddFriendAsync(steamId);
                                        break;
                                    }

                                    if (Regex.IsMatch(s, @"^(?i)STEAM(?-i)_\d:\d:\d{8}$"))
                                    {
                                        result = await _steamClientService.AddFriendAsync(new SteamID(s));
                                        break;
                                    }

                                    result = await _steamClientService.AddFriendAsync(s);
                                } while (false);

                                if (result == null)
                                    return;

                                switch (result.Result)
                                {
                                    case EResult.OK:
                                        MessengerInstance.Send(new ShowMessageDialogMessage("添加成功",
                                            string.Format("你成功向 {0} 发出了好友请求。", result.PersonaName)));
                                        break;

                                    case EResult.DuplicateName:
                                        MessengerInstance.Send(new ShowMessageDialogMessage("添加失败", "对方已经是你的好友了。"));
                                        break;

                                    case EResult.AccountNotFound:
                                        MessengerInstance.Send(new ShowMessageDialogMessage("添加失败", "指定用户不存在。"));
                                        break;

                                    default:
                                        MessengerInstance.Send(new ShowMessageDialogMessage("添加失败",
                                            result.Result.ToString()));
                                        break;
                                }
                            }
                            catch (TimeoutException)
                            {
                                MessengerInstance.Send(new ShowMessageDialogMessage("添加失败", "连接超时。"));
                            }
                        }));
                }));
            }
        }

        public RelayCommand<IList> RemoveFriend
        {
            get
            {
                return _removeFriend ?? (_removeFriend = new RelayCommand<IList>(friends =>
                {
                    if (friends == null || friends.Count == 0)
                        return;

                    MessengerInstance.Send(new ShowMessageDialogMessageWithCallback("删除好友", "你确认要删除选定好友吗？",
                        MessageDialogStyle.AffirmativeAndNegative, async result =>
                        {
                            if (result == MessageDialogResult.Negative)
                                return;

                            if (await Task<bool>.Factory.StartNew(() =>
                            {
                                var removeTasks = from friend in friends.OfType<SteamClientService.Friend>()
                                    select _steamClientService.RemoveFriendAsync(friend.SteamId);
                                try
                                {
                                    Task.WaitAll(removeTasks.ToArray());
                                    return true;
                                }
                                catch (AggregateException)
                                {
                                    return false;
                                }
                            }))
                            {
                                MessengerInstance.Send(new ShowMessageDialogMessage("删除成功", "选定好友删除成功。"));
                            }
                            else
                            {
                                MessengerInstance.Send(new ShowMessageDialogMessage("删除失败", "部分好友删除请求发送超时。"));
                            }
                        }));
                }));
            }
        }
    }
}