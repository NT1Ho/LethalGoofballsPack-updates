using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace LethalGoofballsPack.WesleyZh
{
    internal static class NavigationText
    {
        private static readonly Dictionary<string,string> labels = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex moon;
        private static readonly Regex weather;
        private static readonly Dictionary<string,string> weatherNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            {"None","无"},{"温和","无"},{"Mild weather","无"},{"Hallowed","圣化"},{"Hurricane","飓风"},
            {"Forsaken","遗弃之境"},{"Minefield","雷区"},{"Earthquakes","地震"},{"Rainy","降雨"},{"Stormy","暴风"},{"Foggy","浓雾"},
            {"Flooded","洪水"},{"Eclipsed","日食"},{"DustClouds","沙尘"},{"Dust clouds","沙尘"}
        };

        // WeatherRegistry abbreviates names to at most five characters in forecast rows.
        private static readonly Dictionary<string,string> forecastNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            {"Dust","沙尘"},{"Hurri","飓风"},{"Storm","暴风"},{"Earth","地震"},{"Forsa","遗弃之境"},
            {"Flood","洪水"},{"Eclip","日食"},{"Hallo","圣化"},{"Minef","雷区"}
        };

        static NavigationText()
        {
            var patterns = new List<string>();
            using (var reader = new StreamReader(typeof(NavigationText).Assembly.GetManifestResourceStream("wesley.glossary")))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var fields = line.Split('\t');
                    // Only the moon section of the project terminology table applies to navigation.
                    if (fields[0] == "Apparatus") break;
                    Add(fields[0],fields[1],patterns);
                }
            }
            Add("End","终点",patterns);
            Add("Researcher_6_x1753B","研究者6号_x1753B",patterns);

            labels["Asteroid"] = labels["Asteroid-13"];
            patterns.Add("Asteroid");
            moon = new Regex(@"(?<![A-Za-z0-9-])(?:" + string.Join("|",patterns) + @")(?![A-Za-z0-9-])",RegexOptions.IgnoreCase);
            weather = new Regex(@"(?<![A-Za-z])(?:" + string.Join("|",weatherNames.Keys.Select(Regex.Escape)) + @")(?![A-Za-z])",RegexOptions.IgnoreCase);
        }

        private static void Add(string en,string zh,List<string> patterns)
        {
            var label=zh+" ("+en+")";
            foreach (var spelling in new[]{label,zh+"（"+en+"）",en+"（"+zh+"）",en,zh})
            {
                labels[spelling]=label;
                patterns.Add(Regex.Escape(spelling));
            }
        }

        internal static string Format(string text,bool route)
        {
            if (string.IsNullOrEmpty(text)) return text;
            bool catalogue = text.Contains("ROUTE") && text.Contains("INFO") || text.Contains("外卫星目录") ||
                Regex.IsMatch(text,@"(?im)^\s*\*\s+(Experimentation|Assurance|Vow)\b");
            bool forecastPage=text.IndexOf("Forecasting weather for ",StringComparison.OrdinalIgnoreCase)>=0;
            var lines=text.Split('\n');
            for(int i=0;i<lines.Length;i++)
            {
                var line=lines[i];
                var plain=Regex.Replace(line,"<[^>]+>","").TrimStart();
                bool entry=plain.StartsWith("Forecasting weather for ",StringComparison.OrdinalIgnoreCase) || (catalogue && plain.StartsWith("*")) || plain.StartsWith("地点") || plain.StartsWith("Location:",StringComparison.OrdinalIgnoreCase) ||
                    plain.StartsWith("Orbiting:",StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(plain,@"^-?\d+(?:-\d+)?\s+[A-Za-z\u3400-\u9fff]");
                bool forecast=Regex.IsMatch(plain,@"(?:weather|forecast|天气|预报)\s*[:：]",RegexOptions.IgnoreCase);
                if(!entry && !route && !forecast && !forecastPage)continue;
                // Rich-text tags and their attributes belong to the other display mods.
                lines[i]=Regex.Replace(line,@"<[^>]+>|[^<]+",m=>{
                    if(m.Value.StartsWith("<"))return m.Value;
                    string span=m.Value;
                    if(entry || route)
                    {
                        span=moon.Replace(span,x=>entry || x.Index==0 || Regex.IsMatch(span.Substring(0,x.Index),@"(?:\d|to|前往|目的地|航向|路线|至|：|:)\s*$",RegexOptions.IgnoreCase) ? labels[x.Value] : x.Value);
                    }
                    if(forecastPage && forecastNames.TryGetValue(span.Trim(),out var shortName))
                        return shortName.PadRight(Math.Max(shortName.Length,span.Length-shortName.Length));
                    return weather.Replace(span,x=>weatherNames[x.Value]);
                });
            }
            return string.Join("\n",lines);
        }
    }

    [HarmonyPatch(typeof(Terminal),"TextPostProcess")]
    internal static class TerminalNavigation
    {
        [HarmonyPostfix,HarmonyPriority(Priority.Last)]
        private static void Format(TerminalNode node,ref string __result)
        {
            __result=NavigationText.Format(__result,node!=null && (node.buyRerouteToMoon>=0 || node.displayPlanetInfo>=0));
        }
    }

    [HarmonyPatch(typeof(StartOfRound),nameof(StartOfRound.SetMapScreenInfoToCurrentLevel))]
    internal static class MonitorNavigation
    {
        [HarmonyPostfix,HarmonyPriority(Priority.Last)]
        private static void Format(StartOfRound __instance)
        {
            var screen=__instance.screenLevelDescription;
            if(screen!=null)screen.text=NavigationText.Format(screen.text,false);
        }
    }
}
