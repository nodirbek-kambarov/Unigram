﻿using System;
using System.Collections.Generic;
using Telegram.Td.Api;
using Unigram.Controls;
using Unigram.Navigation;
using Unigram.Navigation.Services;
using Unigram.Services;
using Unigram.Services.ViewService;
using Unigram.ViewModels;
using Unigram.Views;
using Unigram.Views.Payments;
using Unigram.Views.Popups;
using Unigram.Views.Settings;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.WindowManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Unigram.Common
{
    public class TLNavigationService : NavigationService
    {
        private readonly IProtoService _protoService;
        private readonly IPasscodeService _passcodeService;
        private readonly IViewService _viewService;

        private readonly Dictionary<string, AppWindow> _instantWindows = new Dictionary<string, AppWindow>();

        public TLNavigationService(IProtoService protoService, IViewService viewService, Frame frame, int session, string id)
            : base(frame, session, id)
        {
            _protoService = protoService;
            _passcodeService = TLContainer.Current.Passcode;
            _viewService = viewService;
        }

        public int SessionId => _protoService.SessionId;
        public IProtoService ProtoService => _protoService;

        public async void NavigateToInstant(string url)
        {
            //if (ApiInformation.IsTypePresent("Windows.UI.WindowManagement.AppWindow"))
            //{
            //    _instantWindows.TryGetValue(url, out AppWindow window);
            //    if (window == null)
            //    {
            //        var nav = BootStrapper.Current.NavigationServiceFactory(BootStrapper.BackButton.Ignore, BootStrapper.ExistingContent.Exclude, 0, "0", false);
            //        var frame = BootStrapper.Current.CreateRootElement(nav);
            //        nav.Navigate(typeof(InstantPage), url);

            //        window = await AppWindow.TryCreateAsync();
            //        window.PersistedStateId = "InstantView";
            //        window.TitleBar.ExtendsContentIntoTitleBar = true;
            //        window.Closed += (s, args) =>
            //        {
            //            _instantWindows.Remove(url);
            //            frame = null;
            //            window = null;
            //        };

            //        _instantWindows[url] = window;
            //        ElementCompositionPreview.SetAppWindowContent(window, frame);
            //    }

            //    await window.TryShowAsync();
            //    window.RequestMoveAdjacentToCurrentView();
            //}
            //else
            {
                Navigate(typeof(InstantPage), url);
            }
        }

        public async void NavigateToInvoice(MessageViewModel message)
        {
            var parameters = new ViewServiceParams
            {
                Title = message.Content is MessageInvoice invoice && invoice.ReceiptMessageId == 0 ? Strings.Resources.PaymentCheckout : Strings.Resources.PaymentReceipt,
                Width = 380,
                Height = 580,
                PersistentId = "Payments",
                Content = control =>
                {
                    var nav = BootStrapper.Current.NavigationServiceFactory(BootStrapper.BackButton.Ignore, BootStrapper.ExistingContent.Exclude, SessionId, "Payments" + Guid.NewGuid(), false);
                    nav.Navigate(typeof(PaymentFormPage), state: Navigation.Services.NavigationState.GetInvoice(message.ChatId, message.Id));

                    return BootStrapper.Current.CreateRootElement(nav);

                }
            };

            await _viewService.OpenAsync(parameters);
        }

        public async void NavigateToSender(MessageSender sender)
        {
            if (sender is MessageSenderUser user)
            {
                var response = await ProtoService.SendAsync(new CreatePrivateChat(user.UserId, false));
                if (response is Chat chat)
                {
                    Navigate(typeof(ProfilePage), chat.Id);
                }
            }
            else if (sender is MessageSenderChat chat)
            {
                Navigate(typeof(ProfilePage), chat.ChatId);
            }
        }

        public async void NavigateToChat(Chat chat, long? message = null, long? thread = null, string accessToken = null, NavigationState state = null, bool scheduled = false, bool force = true)
        {
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate privata)
            {
                var user = _protoService.GetUser(privata.UserId);
                if (user == null)
                {
                    return;
                }

                if (user.RestrictionReason.Length > 0)
                {
                    await MessagePopup.ShowAsync(user.RestrictionReason, Strings.Resources.AppName, Strings.Resources.OK);
                    return;
                }
            }
            else if (chat.Type is ChatTypeSupergroup super)
            {
                var supergroup = _protoService.GetSupergroup(super.SupergroupId);
                if (supergroup == null)
                {
                    return;
                }

                if (supergroup.Status is ChatMemberStatusLeft && string.IsNullOrEmpty(supergroup.Username) && !supergroup.HasLocation && !supergroup.HasLinkedChat && !_protoService.IsChatAccessible(chat))
                {
                    await MessagePopup.ShowAsync(Strings.Resources.ChannelCantOpenPrivate, Strings.Resources.AppName, Strings.Resources.OK);
                    return;
                }

                if (supergroup.RestrictionReason.Length > 0)
                {
                    await MessagePopup.ShowAsync(supergroup.RestrictionReason, Strings.Resources.AppName, Strings.Resources.OK);
                    return;
                }
            }

            if (Frame.Content is ChatPage page && chat.Id.Equals((long)CurrentPageParam) && thread == null && !scheduled)
            {
                if (message != null)
                {
                    await page.ViewModel.LoadMessageSliceAsync(null, message.Value);
                }
                else
                {
                    await page.ViewModel.LoadMessageSliceAsync(null, chat.LastMessage?.Id ?? long.MaxValue, VerticalAlignment.Bottom);
                }

                page.ViewModel.TextField?.Focus(FocusState.Programmatic);

                if (App.DataPackages.TryRemove(chat.Id, out DataPackageView package))
                {
                    await page.ViewModel.HandlePackageAsync(package);
                }
            }
            else
            {
                //NavigatedEventHandler handler = null;
                //handler = async (s, args) =>
                //{
                //    Frame.Navigated -= handler;

                //    if (args.Content is DialogPage page1 /*&& chat.Id.Equals((long)args.Parameter)*/)
                //    {
                //        if (message.HasValue)
                //        {
                //            await page1.ViewModel.LoadMessageSliceAsync(null, message.Value);
                //        }
                //    }
                //};

                //Frame.Navigated += handler;

                state ??= new NavigationState();

                if (message != null)
                {
                    state["message_id"] = message.Value;
                }

                if (accessToken != null)
                {
                    state["access_token"] = accessToken;
                }

                var ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
                var shift = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
                if (shift && !ctrl)
                {
                    Type target;
                    object parameter;

                    if (thread != null)
                    {
                        target = typeof(ChatThreadPage);
                        parameter = $"{chat.Id};{thread}";
                    }
                    else if (scheduled)
                    {
                        target = typeof(ChatScheduledPage);
                        parameter = chat.Id;
                    }
                    else
                    {
                        target = typeof(ChatPage);
                        parameter = chat.Id;
                    }

                    await OpenAsync(target, parameter);
                }
                else
                {
                    if (Frame.Content is ChatPage chatPage && thread == null && !scheduled && !force)
                    {
                        chatPage.ViewModel.OnNavigatingFrom(null);

                        chatPage.Dispose();
                        chatPage.Activate();
                        chatPage.ViewModel.NavigationService = this;
                        chatPage.ViewModel.Dispatcher = Dispatcher;
                        await chatPage.ViewModel.OnNavigatedToAsync(chat.Id, Windows.UI.Xaml.Navigation.NavigationMode.New, state);

                        FrameFacade.RaiseNavigated(chat.Id);
                    }
                    else
                    {
                        Type target;
                        object parameter;

                        if (thread != null)
                        {
                            target = typeof(ChatThreadPage);
                            parameter = $"{chat.Id};{thread}";
                        }
                        else if (scheduled)
                        {
                            target = typeof(ChatScheduledPage);
                            parameter = chat.Id;
                        }
                        else
                        {
                            target = typeof(ChatPage);
                            parameter = chat.Id;
                        }

                        Navigate(target, parameter, state);
                    }
                }
            }
        }

        public async void NavigateToChat(long chatId, long? message = null, long? thread = null, string accessToken = null, NavigationState state = null, bool scheduled = false, bool force = true)
        {
            var chat = _protoService.GetChat(chatId);
            if (chat == null)
            {
                chat = await _protoService.SendAsync(new GetChat(chatId)) as Chat;
            }

            if (chat == null)
            {
                return;
            }

            NavigateToChat(chat, message, thread, accessToken, state, scheduled, force);
        }

        public async void NavigateToPasscode()
        {
            if (_passcodeService.IsEnabled)
            {
                var dialog = new SettingsPasscodeConfirmPopup();

                var confirm = await dialog.ShowQueuedAsync();
                if (confirm == ContentDialogResult.Primary)
                {
                    Navigate(typeof(SettingsPasscodePage));
                }
            }
            else
            {
                Navigate(typeof(SettingsPasscodePage));
            }
        }
    }
}
