﻿using Microsoft.Toolkit.Uwp.UI.Controls;
using Newtonsoft.Json;
using SmartLensDownloaderProvider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Radios;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Notifications;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace SmartLens
{
    public sealed partial class WebPage : Page, IDisposable
    {
        private bool CanCancelLoading;
        public bool IsPressedFavourite;
        public WebView WebBrowser = null;
        public TabViewItem ThisTab;
        private bool IsRefresh = false;

        public WebPage(Uri uri = null)
        {
            InitializeComponent();
            FavouriteList.ItemsSource = WebTab.ThisPage.FavouriteCollection;
            DownloadList.ItemsSource = SmartLensDownloader.DownloadList;

        //由于未知原因此处new WebView时，若选择多进程模型则可能会引发异常
        FLAG:
            try
            {
                WebBrowser = new WebView(WebViewExecutionMode.SeparateProcess);
            }
            catch (Exception)
            {
                goto FLAG;
            }
            InitHistoryList();
            InitializeWebView();
            Loaded += WebPage_Loaded;

            if (uri != null)
            {
                WebBrowser.Navigate(uri);
            }
        }

        /// <summary>
        /// 初始化历史记录列表
        /// </summary>
        private void InitHistoryList()
        {
            //根据WebTab提供的分类信息决定历史记录树应当展示多少分类
            if (WebTab.ThisPage.HistoryFlag.HasFlag(HistoryTreeCategoryFlag.Today))
            {
                HistoryTree.RootNodes.Add(new TreeViewNode
                {
                    Content = new WebSiteItem("今天", string.Empty),
                    HasUnrealizedChildren = true,
                    IsExpanded = true
                });
            }

            if (WebTab.ThisPage.HistoryFlag.HasFlag(HistoryTreeCategoryFlag.Yesterday))
            {
                HistoryTree.RootNodes.Add(new TreeViewNode
                {
                    Content = new WebSiteItem("昨天", string.Empty),
                    HasUnrealizedChildren = true
                });
            }

            if (WebTab.ThisPage.HistoryFlag.HasFlag(HistoryTreeCategoryFlag.Earlier))
            {
                HistoryTree.RootNodes.Add(new TreeViewNode
                {
                    Content = new WebSiteItem("更早", string.Empty),
                    HasUnrealizedChildren = true
                });
            }

            //遍历HistoryCollection集合以向历史记录树中对应分类添加子对象
            foreach (var HistoryItem in WebTab.ThisPage.HistoryCollection)
            {
                if (HistoryItem.Key == DateTime.Today.AddDays(-1))
                {
                    var TreeNode = from Item in HistoryTree.RootNodes
                                   where (Item.Content as WebSiteItem).Subject == "昨天"
                                   select Item;
                    TreeNode.FirstOrDefault()?.Children.Add(new TreeViewNode
                    {
                        Content = HistoryItem.Value,
                        HasUnrealizedChildren = false,
                        IsExpanded = false
                    });

                }
                else if (HistoryItem.Key == DateTime.Today)
                {
                    var TreeNode = from Item in HistoryTree.RootNodes
                                   where (Item.Content as WebSiteItem).Subject == "今天"
                                   select Item;
                    TreeNode.FirstOrDefault()?.Children.Add(new TreeViewNode
                    {
                        Content = HistoryItem.Value,
                        HasUnrealizedChildren = false,
                        IsExpanded = false
                    });
                }
                else
                {
                    var TreeNode = from Item in HistoryTree.RootNodes
                                   where (Item.Content as WebSiteItem).Subject == "更早"
                                   select Item;
                    TreeNode.FirstOrDefault()?.Children.Add(new TreeViewNode
                    {
                        Content = HistoryItem.Value,
                        HasUnrealizedChildren = false,
                        IsExpanded = false
                    });
                }
            }
        }

        private async void WebPage_Loaded(object sender, RoutedEventArgs e)
        {
            //Loaded在每次切换至当前标签页时都会得到执行，因此在此处可以借机同步不同标签页之间的数据
            //包括其他标签页向收藏列表新增的条目，或其他标签页通过访问网页而向历史记录添加的新条目

            //确定历史记录或收藏列表是否为空，若空则显示“无内容”提示标签
            FavEmptyTips.Visibility = WebTab.ThisPage.FavouriteCollection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryEmptyTips.Visibility = WebTab.ThisPage.HistoryCollection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            DownloadEmptyTips.Visibility = SmartLensDownloader.DownloadList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            //其他标签页已执行清空历史记录时，当前标签页也必须删除历史记录树内的所有节点
            if (WebTab.ThisPage.HistoryCollection.Count == 0)
            {
                HistoryTree.RootNodes.Clear();
            }
            else
            {
                //寻找分类标题为“今天”的节点，与HistoryCollection内的数量进行比对，若不同则发生了变动
                var TreeNodes = from Item in HistoryTree.RootNodes
                                where (Item.Content as WebSiteItem).Subject == "今天"
                                select Item;
                if (TreeNodes.Count() > 0)
                {
                    var Node = TreeNodes.First();
                    if (Node.Children.Count != WebTab.ThisPage.HistoryCollection.Count)
                    {
                        Node.Children.Clear();

                        foreach (var HistoryItem in WebTab.ThisPage.HistoryCollection)
                        {
                            Node.Children.Add(new TreeViewNode
                            {
                                Content = HistoryItem.Value,
                                HasUnrealizedChildren = false,
                                IsExpanded = false
                            });
                        }
                    }
                }
            }

            //以下为检索各存储设置以同步各标签页之间对设置界面选项的更改
            if (ApplicationData.Current.LocalSettings.Values["WebTabOpenMethod"] is string Method)
            {
                foreach (var Item in from string Item in TabOpenMethod.Items
                                     where Method == Item
                                     select Item)
                {
                    TabOpenMethod.SelectedItem = Item;
                }
            }
            else
            {
                TabOpenMethod.SelectedIndex = 0;
            }

            if (ApplicationData.Current.LocalSettings.Values["WebTabMainPage"] is string MainPage)
            {
                MainUrl.Text = MainPage;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["WebTabMainPage"] = "https://www.baidu.com";
                MainUrl.Text = "https://www.baidu.com";
            }

            if (ApplicationData.Current.LocalSettings.Values["WebTabSpecifiedPage"] is string Specified)
            {
                SpecificUrl.Text = Specified;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["WebTabSpecifiedPage"] = "about:blank";
                SpecificUrl.Text = "about:blank";
            }

            if (ApplicationData.Current.LocalSettings.Values["WebShowMainButton"] is bool IsShow)
            {
                ShowMainButton.IsOn = IsShow;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["WebShowMainButton"] = true;
                ShowMainButton.IsOn = true;
            }

            if (ApplicationData.Current.LocalSettings.Values["WebEnableJS"] is bool IsEnableJS)
            {
                AllowJS.IsOn = IsEnableJS;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["WebEnableJS"] = true;
                AllowJS.IsOn = true;
            }

            if (ApplicationData.Current.LocalSettings.Values["WebEnableDB"] is bool IsEnableDB)
            {
                AllowIndexedDB.IsOn = IsEnableDB;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["WebEnableDB"] = true;
                AllowIndexedDB.IsOn = true;
            }

            if (!StorageApplicationPermissions.FutureAccessList.ContainsItem("DownloadPath"))
            {
                StorageFolder Folder = await DownloadsFolder.CreateFolderAsync("SmartLensDownload", CreationCollisionOption.GenerateUniqueName);
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("DownloadPath", Folder);
                DownloadPath.Text = Folder.Path;
            }
            else
            {
                StorageFolder Folder = await StorageApplicationPermissions.FutureAccessList.GetItemAsync("DownloadPath") as StorageFolder;
                DownloadPath.Text = Folder.Path;
            }

            //切换不同标签页时，应当同步InPrivate模式的设置
            //同时因为改变InPrivate设置将导致Toggled事件触发，因此先解除，改变后再绑定
            InPrivate.Toggled -= InPrivate_Toggled;
            if (ApplicationData.Current.LocalSettings.Values["WebActivateInPrivate"] is bool EnableInPrivate)
            {
                InPrivate.IsOn = EnableInPrivate;
                if (EnableInPrivate)
                {
                    Favourite.Visibility = Visibility.Collapsed;
                }
                else
                {
                    Favourite.Visibility = Visibility.Visible;
                }
                InPrivate.Toggled += InPrivate_Toggled;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["WebActivateInPrivate"] = false;
                InPrivate.IsOn = false;
                InPrivate.Toggled += InPrivate_Toggled;
            }

            SmartLensDownloader.DownloadList.CollectionChanged += DownloadList_CollectionChanged;
        }

        private void DownloadList_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            DownloadEmptyTips.Visibility = SmartLensDownloader.DownloadList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void InPrivate_Toggled(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["WebActivateInPrivate"] = InPrivate.IsOn;
            if (InPrivate.IsOn)
            {
                Favourite.Visibility = Visibility.Collapsed;

                if (Resources.TryGetValue("InAppNotificationWithButtonsTemplate", out object NotificationTemplate) && NotificationTemplate is DataTemplate template)
                {
                    InPrivateNotification.Show(template, 10000);
                }
            }
            else
            {
                InPrivateNotification.Dismiss();
                Favourite.Visibility = Visibility.Visible;
                await WebView.ClearTemporaryWebDataAsync();
            }
        }

        /// <summary>
        /// 初始化WebView控件并部署至XAML界面
        /// </summary>
        private void InitializeWebView()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Gr.Children.Add(WebBrowser);
            WebBrowser.SetValue(Grid.RowProperty, 1);
            WebBrowser.SetValue(Canvas.ZIndexProperty, 0);
            WebBrowser.HorizontalAlignment = HorizontalAlignment.Stretch;
            WebBrowser.VerticalAlignment = VerticalAlignment.Stretch;
            WebBrowser.NewWindowRequested += WebBrowser_NewWindowRequested;
            WebBrowser.ContentLoading += WebBrowser_ContentLoading;
            WebBrowser.NavigationCompleted += WebBrowser_NavigationCompleted;
            WebBrowser.NavigationStarting += WebBrowser_NavigationStarting;
            WebBrowser.LongRunningScriptDetected += WebBrowser_LongRunningScriptDetected;
            WebBrowser.UnsafeContentWarningDisplaying += WebBrowser_UnsafeContentWarningDisplaying;
            WebBrowser.ContainsFullScreenElementChanged += WebBrowser_ContainsFullScreenElementChanged;
            WebBrowser.PermissionRequested += WebBrowser_PermissionRequested;
            WebBrowser.SeparateProcessLost += WebBrowser_SeparateProcessLost;
            WebBrowser.NavigationFailed += WebBrowser_NavigationFailed;
            WebBrowser.UnviewableContentIdentified += WebBrowser_UnviewableContentIdentified;
        }

        private void WebBrowser_UnviewableContentIdentified(WebView sender, WebViewUnviewableContentIdentifiedEventArgs args)
        {
            string URL = args.Referrer.ToString();
            string FileName = URL.Substring(URL.LastIndexOf("/") + 1);

            DownloadNotification.Show(GenerateDownloadNotificationTemplate(FileName, args.Referrer));
        }

        private Grid GenerateDownloadNotificationTemplate(string FileName, Uri Refer)
        {
            Grid GridControl = new Grid();

            GridControl.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            GridControl.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            TextBlock textBlock = new TextBlock
            {
                Text = "是否保存文件 " + FileName + " 至本地计算机?\r发布者：" + (string.IsNullOrWhiteSpace(Refer.Host) ? "Unknown" : Refer.Host),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16
            };
            GridControl.Children.Add(textBlock);

            // Buttons part
            StackPanel stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0),
            };

            Button SaveConfirmButton = new Button
            {
                Content = "保存",
                Width = 120,
                Height = 30,
            };
            SaveConfirmButton.Click += async (s, e) =>
            {
                DownloadNotification.Dismiss();
                DownloadControl.IsPaneOpen = true;

                DownloadOperator Operation = await SmartLensDownloader.CreateNewDownloadTask(Refer, FileName);
                Operation.DownloadSucceed += Operation_DownloadSucceed;
                Operation.DownloadErrorDetected += Operation_DownloadErrorDetected;
                Operation.DownloadTaskCancel += Operation_DownloadTaskCancel;

                Operation.StartDownload();

                await SQLite.GetInstance().SetDownloadHistoryAsync(Operation);
            };
            stackPanel.Children.Add(SaveConfirmButton);

            Button CancelButton = new Button
            {
                Content = "取消",
                Width = 120,
                Height = 30,
                Margin = new Thickness(10, 0, 0, 0)
            };
            CancelButton.Click += (s, e) =>
            {
                DownloadNotification.Dismiss();
            };
            stackPanel.Children.Add(CancelButton);

            Grid.SetColumn(stackPanel, 1);
            GridControl.Children.Add(stackPanel);

            return GridControl;
        }

        private async void Operation_DownloadTaskCancel(object sender, DownloadOperator e)
        {
            await SQLite.GetInstance().UpdateDownloadHistoryAsync(e);

            ToastNotificationManager.CreateToastNotifier().Show(e.GenerateToastNotification(ToastNotificationCategory.TaskCancel));

            e.DownloadSucceed -= Operation_DownloadSucceed;
            e.DownloadErrorDetected -= Operation_DownloadErrorDetected;
            e.DownloadTaskCancel -= Operation_DownloadTaskCancel;
        }

        private async void Operation_DownloadErrorDetected(object sender, DownloadOperator e)
        {
            await SQLite.GetInstance().UpdateDownloadHistoryAsync(e);

            ListViewItem Item = DownloadList.ContainerFromItem(e) as ListViewItem;
            Item.ContentTemplate = DownloadErrorTemplate;

            ToastNotificationManager.CreateToastNotifier().Show(e.GenerateToastNotification(ToastNotificationCategory.Error));

            e.DownloadSucceed -= Operation_DownloadSucceed;
            e.DownloadErrorDetected -= Operation_DownloadErrorDetected;
            e.DownloadTaskCancel -= Operation_DownloadTaskCancel;
        }

        private async void Operation_DownloadSucceed(object sender, DownloadOperator e)
        {
            await SQLite.GetInstance().UpdateDownloadHistoryAsync(e);

            ListViewItem Item = DownloadList.ContainerFromItem(e) as ListViewItem;
            Item.ContentTemplate = DownloadCompleteTemplate;

            ToastNotificationManager.CreateToastNotifier().Show(e.GenerateToastNotification(ToastNotificationCategory.Succeed));

            e.DownloadSucceed -= Operation_DownloadSucceed;
            e.DownloadErrorDetected -= Operation_DownloadErrorDetected;
            e.DownloadTaskCancel -= Operation_DownloadTaskCancel;
        }

        private async void WebBrowser_NavigationFailed(object sender, WebViewNavigationFailedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog
            {
                Content = "导航失败，请检查网址或网络连接",
                Title = "提示",
                CloseButtonText = "确定"
            };
            WebBrowser.Navigate(new Uri("about:blank"));
            await dialog.ShowAsync();
        }

        private async void WebBrowser_SeparateProcessLost(WebView sender, WebViewSeparateProcessLostEventArgs args)
        {
            ContentDialog dialog = new ContentDialog
            {
                Content = "浏览器进程意外终止\r将自动重启并返回主页",
                Title = "提示",
                CloseButtonText = "确定"
            };
            await dialog.ShowAsync();
            WebBrowser = new WebView(WebViewExecutionMode.SeparateProcess);
            InitializeWebView();
            WebBrowser.Navigate(new Uri(ApplicationData.Current.LocalSettings.Values["WebTabMainPage"].ToString()));
        }

        private void WebBrowser_ContentLoading(WebView sender, WebViewContentLoadingEventArgs args)
        {
            ThisTab.Header = WebBrowser.DocumentTitle != "" ? WebBrowser.DocumentTitle : "正在加载...";

            AutoSuggest.Text = args.Uri.ToString();

            Back.IsEnabled = WebBrowser.CanGoBack;
            Forward.IsEnabled = WebBrowser.CanGoForward;

            //根据AutoSuggest.Text的内容决定是否改变收藏星星的状态
            if (WebTab.ThisPage.FavouriteDictionary.ContainsKey(AutoSuggest.Text))
            {
                Favourite.Symbol = Symbol.SolidStar;
                Favourite.Foreground = new SolidColorBrush(Colors.Gold);
                IsPressedFavourite = true;
            }
            else
            {
                Favourite.Symbol = Symbol.OutlineStar;
                Favourite.Foreground = new SolidColorBrush(Colors.White);
                IsPressedFavourite = false;
            }

            if (InPrivate.IsOn)
            {
                return;
            }

            //多个标签页可能同时执行至此处，因此引用全局锁对象来确保线程同步
            lock (SyncRootProvider.SyncRoot)
            {
                if (AutoSuggest.Text != "about:blank" && WebBrowser.DocumentTitle != "")
                {
                    var HistoryItems = from Item in WebTab.ThisPage.HistoryCollection
                                       where Item.Value.WebSite == AutoSuggest.Text && Item.Key == DateTime.Today
                                       select Item;
                    for (int i = 0; i < HistoryItems.Count(); i++)
                    {
                        var HistoryItem = HistoryItems.ElementAt(i);
                        if (!HistoryItem.Key.Equals(default))
                        {
                            foreach (var (RootNode, InnerNode) in from RootNode in HistoryTree.RootNodes
                                                                  where (RootNode.Content as WebSiteItem).Subject == "今天"
                                                                  from InnerNode in RootNode.Children
                                                                  where (InnerNode.Content as WebSiteItem).WebSite == HistoryItem.Value.WebSite
                                                                  select (RootNode, InnerNode))
                            {
                                RootNode.Children.Remove(InnerNode);
                                WebTab.ThisPage.HistoryCollection.Remove(HistoryItem);
                                SQLite.GetInstance().DeleteWebHistory(HistoryItem);
                            }
                        }
                    }

                    WebTab.ThisPage.HistoryCollection.Insert(0, new KeyValuePair<DateTime, WebSiteItem>(DateTime.Today, new WebSiteItem(WebBrowser.DocumentTitle, AutoSuggest.Text)));
                }
            }
        }

        private void WebBrowser_NewWindowRequested(WebView sender, WebViewNewWindowRequestedEventArgs args)
        {
            WebPage Web = new WebPage(args.Uri);

            //TabViewItem的Header必须设置否则将导致异常发生
            TabViewItem NewItem = new TabViewItem
            {
                Header = "空白页",
                Icon = new SymbolIcon(Symbol.Document),
                Content = Web
            };
            Web.ThisTab = NewItem;

            WebTab.ThisPage.TabCollection.Add(NewItem);
            WebTab.ThisPage.TabControl.SelectedItem = NewItem;

            //设置此标志以阻止打开外部浏览器
            args.Handled = true;
        }

        public class WebSearchResult
        {
            public string q { get; set; }
            public bool p { get; set; }
            public List<string> s { get; set; }
        }

        /// <summary>
        /// 从baidu搜索建议获取建议的Json字符串
        /// </summary>
        /// <param name="Context">搜索的内容</param>
        /// <returns>Json</returns>
        private string GetJsonFromWeb(string Context)
        {
            string url = "http://suggestion.baidu.com/su?wd=" + Context + "&cb=window.baidu.sug";
            string str;
            try
            {
                Uri uri = new Uri(url);
                HttpWebRequest wr = (HttpWebRequest)WebRequest.Create(uri);
                Stream s = wr.GetResponse().GetResponseStream();
                StreamReader sr = new StreamReader(s, Encoding.GetEncoding("GBK"));
                str = sr.ReadToEnd();
                str = str.Remove(0, 17);
                str = str.Remove(str.Length - 2, 2);
            }
            catch
            {
                return "";
            }
            return str;
        }

        private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput && sender.Text != "")
            {
                if (JsonConvert.DeserializeObject<WebSearchResult>(GetJsonFromWeb(sender.Text)) is WebSearchResult SearchResult)
                {
                    sender.ItemsSource = SearchResult.s;
                }
            }
        }

        private void AutoSuggestBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null)
            {
                WebBrowser.Navigate(new Uri("https://www.baidu.com/s?wd=" + args.ChosenSuggestion.ToString()));
            }
            else
            {
                //尝试创建搜索框键入内容的Uri，若创建失败则并非网址
                if (Uri.TryCreate(args.QueryText, UriKind.Absolute, out Uri uri))
                {
                    WebBrowser.Navigate(uri);
                }
                else
                {
                    WebBrowser.Navigate(new Uri("https://www.baidu.com/s?wd=" + args.QueryText));
                }
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            WebBrowser.GoBack();
            if (WebTab.ThisPage.FavouriteDictionary.ContainsKey(AutoSuggest.Text))
            {
                Favourite.Symbol = Symbol.SolidStar;
                Favourite.Foreground = new SolidColorBrush(Colors.Gold);
                IsPressedFavourite = true;
            }
            else
            {
                Favourite.Symbol = Symbol.OutlineStar;
                Favourite.Foreground = new SolidColorBrush(Colors.White);
                IsPressedFavourite = false;
            }
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            WebBrowser.GoForward();
            if (WebTab.ThisPage.FavouriteDictionary.ContainsKey(AutoSuggest.Text))
            {
                Favourite.Symbol = Symbol.SolidStar;
                Favourite.Foreground = new SolidColorBrush(Colors.Gold);
                IsPressedFavourite = true;
            }
            else
            {
                Favourite.Symbol = Symbol.OutlineStar;
                Favourite.Foreground = new SolidColorBrush(Colors.White);
                IsPressedFavourite = false;
            }
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            string HomeString = ApplicationData.Current.LocalSettings.Values["WebTabMainPage"].ToString();

            if (Uri.TryCreate(HomeString, UriKind.Absolute, out Uri uri))
            {
                WebBrowser.Navigate(uri);
            }
            else
            {
                ContentDialog dialog = new ContentDialog
                {
                    Content = "导航失败，请检查网址或网络连接",
                    Title = "提示",
                    CloseButtonText = "确定"
                };
                _ = dialog.ShowAsync();
                WebBrowser.Navigate(new Uri("about:blank"));
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            IsRefresh = true;
            if (CanCancelLoading)
            {
                WebBrowser.Stop();
                RefreshState.Symbol = Symbol.Refresh;
                ProGrid.Width = new GridLength(8);
                Progress.IsActive = false;
                CanCancelLoading = false;
            }
            else
            {
                WebBrowser.Refresh();
            }
        }

        private void WebBrowser_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            if (ThisTab.Header.ToString() == "正在加载...")
            {
                ThisTab.Header = WebBrowser.DocumentTitle == "" ? "空白页" : WebBrowser.DocumentTitle;
            }

            if (InPrivate.IsOn)
            {
                IsRefresh = false;
                goto FLAG;
            }

            //处理仅刷新的情况。点击刷新时将不会触发ContentLoading事件，因此需要单独处理
            if (IsRefresh)
            {
                lock (SyncRootProvider.SyncRoot)
                {
                    if (AutoSuggest.Text != "about:blank" && WebBrowser.DocumentTitle != "")
                    {
                        var HistoryItems = from Item in WebTab.ThisPage.HistoryCollection
                                           where Item.Value.WebSite == AutoSuggest.Text && Item.Key == DateTime.Today
                                           select Item;
                        for (int i = 0; i < HistoryItems.Count(); i++)
                        {
                            var HistoryItem = HistoryItems.ElementAt(i);
                            if (!HistoryItem.Key.Equals(default))
                            {
                                foreach (var (RootNode, InnerNode) in from RootNode in HistoryTree.RootNodes
                                                                      where (RootNode.Content as WebSiteItem).Subject == "今天"
                                                                      from InnerNode in RootNode.Children
                                                                      where (InnerNode.Content as WebSiteItem).WebSite == HistoryItem.Value.WebSite
                                                                      select (RootNode, InnerNode))
                                {
                                    RootNode.Children.Remove(InnerNode);
                                    WebTab.ThisPage.HistoryCollection.Remove(HistoryItem);
                                    SQLite.GetInstance().DeleteWebHistory(HistoryItem);
                                }
                            }
                        }

                        WebTab.ThisPage.HistoryCollection.Insert(0, new KeyValuePair<DateTime, WebSiteItem>(DateTime.Today, new WebSiteItem(WebBrowser.DocumentTitle, AutoSuggest.Text)));
                    }
                }
                IsRefresh = false;
            }


        FLAG:
            RefreshState.Symbol = Symbol.Refresh;
            ProGrid.Width = new GridLength(8);
            Progress.IsActive = false;
            CanCancelLoading = false;
        }

        private void WebBrowser_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            ProGrid.Width = new GridLength(40);
            Progress.IsActive = true;
            CanCancelLoading = true;
            RefreshState.Symbol = Symbol.Cancel;
        }

        private async void WebBrowser_LongRunningScriptDetected(WebView sender, WebViewLongRunningScriptDetectedEventArgs args)
        {
            if (args.ExecutionTime.TotalMilliseconds >= 5000)
            {
                args.StopPageScriptExecution = true;
                ContentDialog dialog = new ContentDialog
                {
                    Content = "检测到长时间运行的JavaScript脚本，可能会导致应用无响应，已自动终止",
                    Title = "警告",
                    CloseButtonText = "确定"
                };
                await dialog.ShowAsync();
            }
        }

        private async void WebBrowser_UnsafeContentWarningDisplaying(WebView sender, object args)
        {
            ContentDialog dialog = new ContentDialog
            {
                Content = "SmartScreen筛选器将该页面标记为不安全",
                Title = "警告",
                CloseButtonText = "继续访问",
                PrimaryButtonText = "返回主页"
            };
            dialog.PrimaryButtonClick += (s, e) =>
            {
                WebBrowser.Navigate(new Uri(ApplicationData.Current.LocalSettings.Values["WebTabMainPage"].ToString()));
            };
            await dialog.ShowAsync();
        }

        private void WebBrowser_ContainsFullScreenElementChanged(WebView sender, object args)
        {
            var applicationView = ApplicationView.GetForCurrentView();

            if (sender.ContainsFullScreenElement)
            {
                applicationView.TryEnterFullScreenMode();
            }
            else if (applicationView.IsFullScreenMode)
            {
                applicationView.ExitFullScreenMode();
            }
        }

        private async void WebBrowser_PermissionRequested(WebView sender, WebViewPermissionRequestedEventArgs args)
        {
            if (args.PermissionRequest.PermissionType == WebViewPermissionType.Geolocation)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Content = "网站请求获取您的精确GPS定位",
                    Title = "权限",
                    CloseButtonText = "拒绝",
                    PrimaryButtonText = "允许"
                };
                dialog.PrimaryButtonClick += (s, e) =>
                {
                    args.PermissionRequest.Allow();
                };
                dialog.CloseButtonClick += (s, e) =>
                {
                    args.PermissionRequest.Deny();
                };
                await dialog.ShowAsync();
            }
            else if (args.PermissionRequest.PermissionType == WebViewPermissionType.WebNotifications)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Content = "网站请求Web通知权限",
                    Title = "权限",
                    CloseButtonText = "拒绝",
                    PrimaryButtonText = "允许"
                };
                dialog.PrimaryButtonClick += (s, e) =>
                {
                    args.PermissionRequest.Allow();
                };
                dialog.CloseButtonClick += (s, e) =>
                {
                    args.PermissionRequest.Deny();
                };
                await dialog.ShowAsync();
            }
        }

        private async void ScreenShot_Click(object sender, RoutedEventArgs e)
        {
            if ((await (await BluetoothAdapter.GetDefaultAsync()).GetRadioAsync()).State != RadioState.On)
            {
                ContentDialog dialog = new ContentDialog
                {
                    Content = "蓝牙功能尚未开启，是否前往设置开启？",
                    Title = "提示",
                    PrimaryButtonText = "确定",
                    CloseButtonText = "取消"
                };
                if ((await dialog.ShowAsync()) == ContentDialogResult.Primary)
                {
                    MainPage.ThisPage.NavFrame.Navigate(typeof(SettingsPage));
                }
                return;
            }
            IRandomAccessStream stream = new InMemoryRandomAccessStream();
            await WebBrowser.CapturePreviewToStreamAsync(stream);

            BluetoothUI Bluetooth = new BluetoothUI();

            var result = await Bluetooth.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                Bluetooth = null;
                stream.Dispose();
                return;
            }
            else if (result == ContentDialogResult.Primary)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    BluetoothFileTransfer FileTransfer = new BluetoothFileTransfer
                    {
                        StreamToSend = stream.AsStream(),
                        FileName = WebBrowser.DocumentTitle == "" ? "屏幕截图.jpg" : WebBrowser.DocumentTitle + ".jpg",
                        UseStorageFileRatherThanStream = false
                    };
                    await FileTransfer.ShowAsync();
                });
            }
        }

        private async void ClearCache_Click(object sender, RoutedEventArgs e)
        {
            TipsFly.Hide();

            await WebView.ClearTemporaryWebDataAsync();
            await SQLite.GetInstance().ClearTableAsync("WebHistory");
            WebTab.ThisPage.HistoryCollection.Clear();
            HistoryTree.RootNodes.Clear();

            ContentDialog dialog = new ContentDialog
            {
                Content = "所有缓存和历史记录数据均已清空",
                Title = "提示",
                CloseButtonText = "确定"
            };
            await dialog.ShowAsync();
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            ContentDialog dialog = new ContentDialog
            {
                Content = "SmartLens浏览器\r\r具备SmartScreen保护和完整权限控制\r\r基于Microsoft Edge内核的轻型浏览器",
                Title = "关于",
                CloseButtonText = "确定"
            };
            await dialog.ShowAsync();
        }

        public async void Dispose()
        {
            if (WebBrowser != null)
            {
                await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                {
                    WebBrowser.NewWindowRequested -= WebBrowser_NewWindowRequested;
                    WebBrowser.ContentLoading -= WebBrowser_ContentLoading;
                    WebBrowser.NavigationCompleted -= WebBrowser_NavigationCompleted;
                    WebBrowser.NavigationStarting -= WebBrowser_NavigationStarting;
                    WebBrowser.LongRunningScriptDetected -= WebBrowser_LongRunningScriptDetected;
                    WebBrowser.UnsafeContentWarningDisplaying -= WebBrowser_UnsafeContentWarningDisplaying;
                    WebBrowser.ContainsFullScreenElementChanged -= WebBrowser_ContainsFullScreenElementChanged;
                    WebBrowser.PermissionRequested -= WebBrowser_PermissionRequested;
                    WebBrowser.SeparateProcessLost -= WebBrowser_SeparateProcessLost;
                    WebBrowser.NavigationFailed -= WebBrowser_NavigationFailed;
                    WebBrowser = null;
                });
            }
            ThisTab = null;
            InPrivate.Toggled -= InPrivate_Toggled;
            SmartLensDownloader.DownloadList.CollectionChanged -= DownloadList_CollectionChanged;
        }

        private void FavoutiteListButton_Click(object sender, RoutedEventArgs e)
        {
            SplitControl.IsPaneOpen = !SplitControl.IsPaneOpen;
        }

        private void Favourite_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (IsPressedFavourite)
            {
                return;
            }
            Favourite.Foreground = new SolidColorBrush(Colors.Gold);
        }

        private void Favourite_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (IsPressedFavourite)
            {
                return;
            }
            Favourite.Foreground = new SolidColorBrush(Colors.White);
        }

        private async void Favourite_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (ThisTab.Header.ToString() == "空白页")
            {
                return;
            }

            if (Favourite.Symbol == Symbol.SolidStar)
            {
                IsPressedFavourite = false;
                Favourite.Symbol = Symbol.OutlineStar;
                Favourite.Foreground = new SolidColorBrush(Colors.White);

                if (WebTab.ThisPage.FavouriteDictionary.ContainsKey(AutoSuggest.Text))
                {
                    var FavItem = WebTab.ThisPage.FavouriteDictionary[AutoSuggest.Text];
                    WebTab.ThisPage.FavouriteCollection.Remove(FavItem);
                    WebTab.ThisPage.FavouriteDictionary.Remove(FavItem.WebSite);

                    await SQLite.GetInstance().DeleteWebFavouriteListAsync(FavItem);
                }
            }
            else
            {
                FavName.Text = WebBrowser.DocumentTitle;
                FavName.SelectAll();
                FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
            }
        }

        private void Setting_Click(object sender, RoutedEventArgs e)
        {
            SettingControl.IsPaneOpen = !SettingControl.IsPaneOpen;
        }

        private void TabOpenMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["WebTabOpenMethod"] = TabOpenMethod.SelectedItem.ToString();
            SpecificUrl.Visibility = TabOpenMethod.SelectedItem.ToString() == "特定页" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowMainButton_Toggled(object sender, RoutedEventArgs e)
        {
            ApplicationData.Current.LocalSettings.Values["WebShowMainButton"] = ShowMainButton.IsOn;
            if (ShowMainButton.IsOn)
            {
                Home.Visibility = Visibility.Visible;
                HomeGrid.Width = new GridLength(50);
            }
            else
            {
                Home.Visibility = Visibility.Collapsed;
                HomeGrid.Width = new GridLength(0);
            }
        }

        private void AllowJS_Toggled(object sender, RoutedEventArgs e)
        {
            if (WebBrowser == null)
            {
                return;
            }
            ApplicationData.Current.LocalSettings.Values["WebEnableJS"] = AllowJS.IsOn;
            WebBrowser.Settings.IsJavaScriptEnabled = AllowJS.IsOn;
        }

        private void AllowIndexedDB_Toggled(object sender, RoutedEventArgs e)
        {
            if (WebBrowser == null)
            {
                return;
            }
            ApplicationData.Current.LocalSettings.Values["WebEnableDB"] = AllowIndexedDB.IsOn;
            WebBrowser.Settings.IsIndexedDBEnabled = AllowIndexedDB.IsOn;
        }

        private void SettingControl_PaneClosed(SplitView sender, object args)
        {
            //设置面板关闭时保存所有设置内容

            if (string.IsNullOrWhiteSpace(MainUrl.Text))
            {
                ApplicationData.Current.LocalSettings.Values["WebTabMainPage"] = "about:blank";
            }
            else
            {
                if (MainUrl.Text.StartsWith("http"))
                {
                    ApplicationData.Current.LocalSettings.Values["WebTabMainPage"] = MainUrl.Text;
                }
                else
                {
                    ApplicationData.Current.LocalSettings.Values["WebTabMainPage"] = "http://" + MainUrl.Text;
                }
            }

            if (string.IsNullOrWhiteSpace(SpecificUrl.Text))
            {
                ApplicationData.Current.LocalSettings.Values["WebTabSpecifiedPage"] = "about:blank";
            }
            else
            {
                if (SpecificUrl.Text == "about:blank")
                {
                    ApplicationData.Current.LocalSettings.Values["WebTabSpecifiedPage"] = SpecificUrl.Text;
                    return;
                }
                if (SpecificUrl.Text.StartsWith("http"))
                {
                    ApplicationData.Current.LocalSettings.Values["WebTabSpecifiedPage"] = SpecificUrl.Text;
                }
                else
                {
                    ApplicationData.Current.LocalSettings.Values["WebTabSpecifiedPage"] = "http://" + SpecificUrl.Text;
                }
            }

        }

        private async void SaveConfirm_Click(object sender, RoutedEventArgs e)
        {
            Fly.Hide();

            IsPressedFavourite = true;
            Favourite.Symbol = Symbol.SolidStar;
            Favourite.Foreground = new SolidColorBrush(Colors.Gold);

            if (!WebTab.ThisPage.FavouriteDictionary.ContainsKey(AutoSuggest.Text))
            {
                var FavItem = new WebSiteItem(FavName.Text, AutoSuggest.Text);
                WebTab.ThisPage.FavouriteCollection.Add(FavItem);
                WebTab.ThisPage.FavouriteDictionary.Add(AutoSuggest.Text, FavItem);

                await SQLite.GetInstance().SetWebFavouriteListAsync(FavItem);
            }

        }

        private void FavName_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveConfirm.IsEnabled = !string.IsNullOrWhiteSpace(FavName.Text);
        }

        private void SaveCancel_Click(object sender, RoutedEventArgs e)
        {
            Fly.Hide();
        }

        private void FavouriteList_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var Context = (e.OriginalSource as FrameworkElement)?.DataContext as WebSiteItem;
            FavouriteList.SelectedItem = Context;
        }

        private void FavouriteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Delete.IsEnabled = FavouriteList.SelectedIndex != -1;
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            var FavItem = FavouriteList.SelectedItem as WebSiteItem;
            if (AutoSuggest.Text == FavItem.WebSite)
            {
                Favourite.Symbol = Symbol.OutlineStar;
                Favourite.Foreground = new SolidColorBrush(Colors.White);
                IsPressedFavourite = false;
            }

            WebTab.ThisPage.FavouriteCollection.Remove(FavItem);
            WebTab.ThisPage.FavouriteDictionary.Remove(FavItem.WebSite);

            await SQLite.GetInstance().DeleteWebFavouriteListAsync(FavItem);
        }

        private void FavouriteList_ItemClick(object sender, ItemClickEventArgs e)
        {
            WebBrowser.Navigate(new Uri((e.ClickedItem as WebSiteItem).WebSite));
        }

        private void HistoryTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            var WebItem = (args.InvokedItem as TreeViewNode).Content as WebSiteItem;
            WebBrowser.Navigate(new Uri(WebItem.WebSite));
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            InPrivateNotification.Dismiss();
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            InPrivate.IsOn = false;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            TipsFly.Hide();
        }

        private void SettingControl_PaneOpening(SplitView sender, object args)
        {
            Scroll.ChangeView(null, 0, null);
        }

        private void TextBlock_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout((FrameworkElement)sender);
        }

        private async void ClearData_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Name == "ClearFav")
            {
                ClearFavFly.Hide();
                await SQLite.GetInstance().ClearTableAsync("WebFavourite");

                foreach (var Web in from Tab in WebTab.ThisPage.TabCollection
                                    let Web = Tab.Content as WebPage
                                    where WebTab.ThisPage.FavouriteDictionary.ContainsKey(Web.AutoSuggest.Text)
                                    select Web)
                {
                    Web.Favourite.Symbol = Symbol.OutlineStar;
                    Web.Favourite.Foreground = new SolidColorBrush(Colors.White);
                    Web.IsPressedFavourite = false;
                }

                WebTab.ThisPage.FavouriteCollection.Clear();
                WebTab.ThisPage.FavouriteDictionary.Clear();
            }
            else
            {
                ClearHistoryFly.Hide();
                await SQLite.GetInstance().ClearTableAsync("WebHistory");
                WebTab.ThisPage.HistoryCollection.Clear();
                HistoryTree.RootNodes.Clear();
            }
        }

        private void CancelClear_Click(object sender, RoutedEventArgs e)
        {
            ClearFavFly.Hide();
            ClearHistoryFly.Hide();
        }

        private void DownloadListButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadControl.IsPaneOpen = !DownloadControl.IsPaneOpen;
        }

        private void PauseDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            Button btn = (Button)sender;
            DownloadList.SelectedItem = btn.DataContext;

            if (btn.Content.ToString() == "暂停")
            {
                ListViewItem Item = DownloadList.ContainerFromItem(DownloadList.SelectedItem) as ListViewItem;
                Item.ContentTemplate = DownloadPauseTemplate;

                SmartLensDownloader.DownloadList[DownloadList.SelectedIndex].PauseDownload();
            }
            else
            {
                ListViewItem Item = DownloadList.ContainerFromItem(DownloadList.SelectedItem) as ListViewItem;
                Item.ContentTemplate = DownloadingTemplate;

                SmartLensDownloader.DownloadList[DownloadList.SelectedIndex].ResumeDownload();
            }
        }

        private void StopDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            DownloadList.SelectedItem = ((Button)sender).DataContext;

            ListViewItem Item = DownloadList.ContainerFromItem(DownloadList.SelectedItem) as ListViewItem;
            Item.ContentTemplate = DownloadCancelTemplate;

            var Operation = SmartLensDownloader.DownloadList[DownloadList.SelectedIndex];
            Operation.StopDownload();
        }

        private async void SetDownloadPathButton_Click(object sender, RoutedEventArgs e)
        {
            FolderPicker SavePicker = new FolderPicker
            {
                CommitButtonText = "保存",
                SuggestedStartLocation = PickerLocationId.Downloads,
                ViewMode = PickerViewMode.List
            };
            SavePicker.FileTypeFilter.Add(".exe");
            StorageFolder SaveFolder = await SavePicker.PickSingleFolderAsync();

            if (SaveFolder != null)
            {
                DownloadPath.Text = SaveFolder.Path;
                StorageApplicationPermissions.FutureAccessList.AddOrReplace("DownloadPath", SaveFolder);
            }
        }

        private void CloseDownloadItemButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            ((SymbolIcon)sender).Foreground = new SolidColorBrush(Colors.OrangeRed);
        }

        private void CloseDownloadItemButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ((SymbolIcon)sender).Foreground = new SolidColorBrush(Colors.White);
        }

        private async void CloseDownloadItemButton_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            DownloadList.SelectedItem = ((SymbolIcon)sender).DataContext;

            var Operation = SmartLensDownloader.DownloadList[DownloadList.SelectedIndex];
            if (Operation.State == DownloadState.Downloading || Operation.State == DownloadState.Paused)
            {
                Operation.StopDownload();
            }

            SmartLensDownloader.DownloadList.RemoveAt(DownloadList.SelectedIndex);

            await SQLite.GetInstance().DeleteDownloadHistoryAsync(Operation);
        }

    }
}
