﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;


namespace SmartLens
{
    public partial class Lrc : UserControl
    {
        public class LrcModel
        {
            /// <summary>
            /// 歌词所在控件
            /// </summary>
            public TextBlock LrcTb { get; set; }

            /// <summary>
            /// 歌词字符串
            /// </summary>
            public string LrcText { get; set; }

            /// <summary>
            /// 时间
            /// </summary>
            public double Time { get; set; }
        }
        //歌词集合
        public Dictionary<double, LrcModel> Lrcs = new Dictionary<double, LrcModel>();

        //添加当前焦点歌词变量
        public LrcModel FoucsLrc { get; set; }

        //歌词翻译集合
        Dictionary<TimeSpan, string> TLrcs = new Dictionary<TimeSpan, string>();
        //非焦点歌词颜色
        public SolidColorBrush NoramlLrcColor = new SolidColorBrush(Colors.Black);
        //焦点歌词颜色
        public SolidColorBrush FoucsLrcColor = new SolidColorBrush(Colors.Orange);

        public List<KeyValuePair<double, LrcModel>> SortLrcs;

        int LastIndex = -1;

        public Lrc()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 加载并划分歌词
        /// </summary>
        /// <param name="LrcString">歌词原文</param>
        /// <param name="TranslateLrcString">歌词翻译(若有)</param>
        public void LoadLrc(string LrcString, string TranslateLrcString)
        {
            Lrcs.Clear();
            TLrcs.Clear();
            string[] TlrcCollection = null;
            if (TranslateLrcString != null)
            {
                TlrcCollection = TranslateLrcString.Split('\n');

                foreach (var item in TlrcCollection.Where(item => item.Length > 0 && item.IndexOf(":") != -1).Select(item => item))
                {
                    TimeSpan time = GetTime(item);
                    if (time == TimeSpan.MaxValue)
                    {
                        continue;
                    }

                    TLrcs.Add(time, item.Split(']')[1]);
                }
            }

            string[] StrCollection = LrcString.Split('\n');

            for (int i = 0; i < StrCollection.Length; i++)
            {
                string str = StrCollection[i];
                if (str.Length > 0 && str.IndexOf(":") != -1)
                {
                    TimeSpan time = GetTime(str);
                    if (time == TimeSpan.MaxValue)
                    {
                        continue;
                    }
                    string lrc = string.Empty;
                    if (TlrcCollection == null)
                    {
                        lrc = str.Split(']')[1];
                    }
                    else
                    {
                        if (TLrcs.ContainsKey(time))
                        {

                            lrc = str.Split(']')[1] + "\r" + TLrcs[time];
                        }
                        else
                        {
                            lrc = str.Split(']')[1];
                        }
                    }

                    TextBlock c_lrcbk = new TextBlock
                    {
                        FontSize = 15,
                        Text = lrc,
                        Foreground = NoramlLrcColor,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    if (c_lrc_items.Children.Count > 0)
                    {
                        c_lrcbk.Margin = new Thickness(0, 25, 0, 0);
                    }

                    try
                    {
                        Lrcs.Add(time.TotalMilliseconds, new LrcModel()
                        {
                            LrcTb = c_lrcbk,
                            LrcText = lrc,
                            Time = time.TotalMilliseconds
                        });
                    }
                    catch (ArgumentException)
                    {
                        Lrcs[time.TotalMilliseconds] = new LrcModel()
                        {
                            LrcTb = c_lrcbk,
                            LrcText = lrc,
                            Time = time.TotalMilliseconds
                        };
                    }

                    c_lrc_items.Children.Add(c_lrcbk);

                }
            }

            SortLrcs = new List<KeyValuePair<double, LrcModel>>(Lrcs.AsEnumerable());
            SortLrcs.Sort((x, y) => x.Key.CompareTo(y.Key));

        }

        /// <summary>
        /// 使用正则表达式提取时间
        /// </summary>
        /// <param name="str">Lrc歌词</param>
        /// <returns></returns>
        public TimeSpan GetTime(string str)
        {
            Regex reg = new Regex(@"\[(?<time>.*)\]", RegexOptions.IgnoreCase);
            string timestr = reg.Match(str).Groups["time"].Value;
            int m;
            //获得分
            try
            {
                m = Convert.ToInt32(timestr.Split(':')[0]);
            }
            catch (Exception)
            {
                return TimeSpan.MaxValue;
            }
            //判断是否有小数点
            int f = 0;
            int s;
            if (timestr.Split(':')[1].IndexOf(".") != -1)
            {
                //有
                s = Convert.ToInt32(timestr.Split(':')[1].Split('.')[0]);
                try
                {
                    //获得毫秒位
                    f = Convert.ToInt32(timestr.Split(':')[1].Split('.')[1]);
                }
                catch (Exception)
                {
                    f = 0;
                }

            }
            else
            {
                //没有
                s = Convert.ToInt32(timestr.Split(':')[1]);

            }
            return new TimeSpan(0, 0, m, s, f);
        }

        /// <summary>
        /// 滚动歌词并定位焦点
        /// </summary>
        /// <param name="nowtime">当前时间</param>
        public void LrcRoll(double nowtime)
        {
            if (Lrcs.Count == 0)
            {
                return;
            }
            if (FoucsLrc == null)
            {
                FoucsLrc = Lrcs.Values.First();
            }
            else
            {
                int index = SortLrcs.FindIndex(m => m.Key >= nowtime);
                if (index <= 0 || index == LastIndex)
                {
                    return;
                }

                FoucsLrc.LrcTb.FontSize = 15;

                LastIndex = index;
                LrcModel lm = SortLrcs[index - 1].Value;

                FoucsLrc.LrcTb.Foreground = NoramlLrcColor;

                FoucsLrc = lm;
                FoucsLrc.LrcTb.Foreground = FoucsLrcColor;
                FoucsLrc.LrcTb.FontSize = 20;
                ResetLrcviewScroll();
            }

        }

        /// <summary>
        /// 调整歌词控件滚动条位置
        /// </summary>
        public void ResetLrcviewScroll()
        {
            GeneralTransform gf = FoucsLrc.LrcTb.TransformToVisual(c_lrc_items);
            Point p = gf.TransformPoint(new Point(0, 0));
            double os = p.Y - (c_scrollviewer.ActualHeight / 2) + 10;
            c_scrollviewer.ChangeView(c_scrollviewer.HorizontalOffset, os, 1);
        }
    }
}
