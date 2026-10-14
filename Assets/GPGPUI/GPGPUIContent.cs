using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace GPGPUI
{
    public readonly struct InfoCard
    {
        public readonly string Heading;
        public readonly string Body;
        public readonly string Source;
        public readonly string Headline;
        public readonly string PrimaryMetricLabel;
        public readonly float PrimaryMetricFraction;
        public readonly string SecondaryMetricLabel;
        public readonly float SecondaryMetricFraction;

        public bool HasComparison => !string.IsNullOrEmpty(PrimaryMetricLabel) &&
            !string.IsNullOrEmpty(SecondaryMetricLabel);

        public InfoCard(string heading, string body, string source, string headline = null,
            string primaryMetricLabel = null, float primaryMetricFraction = 0f,
            string secondaryMetricLabel = null, float secondaryMetricFraction = 0f)
        {
            Heading = heading;
            Body = body;
            Source = source;
            Headline = headline;
            PrimaryMetricLabel = primaryMetricLabel;
            PrimaryMetricFraction = primaryMetricFraction;
            SecondaryMetricLabel = secondaryMetricLabel;
            SecondaryMetricFraction = secondaryMetricFraction;
        }
    }

    /// <summary>Visitor copy and research context for the exhibit UI.</summary>
    public static class GPGPUIContent
    {
        const string NoaaMarineDebris = "NOAA Marine Debris Program";
        const string SizeStudy = "Lebreton et al., Scientific Reports (2018)";

        // Primary sources:
        // https://oceanservice.noaa.gov/facts/garbagepatch.html
        // https://marinedebris.noaa.gov/discover-marine-debris/garbage-patches
        // https://www.nature.com/articles/s41598-018-22939-w
        public static readonly InfoCard[] OverviewCards =
        {
            new InfoCard("What is the patch?",
                "Ocean currents concentrate floating debris between Hawaii and California. The patch is a shifting region of dispersed plastic, much of it difficult to see.",
                NoaaMarineDebris),
            new InfoCard("Estimated area",
                "Estimated area of the floating-plastic accumulation zone in the 2018 study: more than twice the size of Texas.",
                SizeStudy, "1.6 million km\u00b2"),
            new InfoCard("Estimated pieces",
                "Estimated number of floating plastic pieces within the 2018 study area.",
                SizeStudy, "1.8 trillion"),
            new InfoCard("Estimated mass",
                "Estimated mass of floating plastic within the 2018 study area.",
                SizeStudy, "~79,000 metric tons"),
            new InfoCard("Read the exhibit",
                "The four bands organize plastic by size; their thickness represents estimated mass share, not ocean depth. Models are illustrative and enlarged for visibility.",
                "Exhibit visualization; " + SizeStudy)
        };

        // https://marinedebris.noaa.gov/why-marine-debris-problem/wildlife-entanglement-and-ghost-fishing
        // https://marinedebris.noaa.gov/why-marine-debris-problem/ingestion
        public static readonly InfoCard[] WildlifeCards =
        {
            new InfoCard("Entanglement",
                "Lost fishing nets and lines can trap wildlife. An entangled turtle may struggle to swim and feed.",
                NoaaMarineDebris),
            new InfoCard("Ingestion",
                "Animals can mistake plastic for food. Swallowed debris can cause injury or block digestion, preventing animals from getting the nutrients they need.",
                NoaaMarineDebris)
        };

        // https://marinedebris.noaa.gov/discover-marine-debris/how-help
        // https://marinedebris.noaa.gov/how-help/home
        public static readonly InfoCard[] ActionCards =
        {
            new InfoCard("Reuse",
                "Choose reusable bottles, bags, and food containers. Reuse or repair items before replacing them.",
                NoaaMarineDebris),
            new InfoCard("Dispose",
                "Check your local recycling rules. Keep food and nonrecyclable materials out of the recycling bin.",
                NoaaMarineDebris),
            new InfoCard("Cleanup",
                "Join a local cleanup to remove litter from streets, waterways, or beaches before it becomes marine debris.",
                NoaaMarineDebris)
        };

        // Source order matches GPGPLayeredCubeVisualization.debrisModelGroups.
        // These are the 2018 study's measured size classes. Its microplastic
        // range starts at 0.5 mm and is narrower than the general <5 mm definition.
        static readonly string[] CategoryNames =
        {
            "Microplastic", "Mesoplastic", "Macroplastic", "Megaplastic"
        };

        static readonly string[] CategoryRanges =
        {
            "0.5-5 mm", "5-50 mm", "5-50 cm", ">50 cm"
        };

        static readonly string[] CategoryDescriptions =
        {
            "Tiny floating fragments can be difficult to see.",
            "Small pieces can include broken plastic and bottle caps.",
            "Larger pieces can include bottles, containers, and broken objects.",
            "Large debris includes lost nets, ropes, and bulky plastic objects."
        };

        // Research facts describe study samples, never the origin or age of an
        // illustrative model. The final card explains the exhibit's visual scale.
        static readonly InfoCard[][] CategoryCards =
        {
            new[]
            {
                new InfoCard("Microplastic", "Tiny floating fragments from 0.5 to 5 mm can be difficult to see.", SizeStudy),
                new InfoCard("Many pieces, little mass",
                    "Shares of estimated floating plastic in the study area.",
                    SizeStudy, primaryMetricLabel: "94% of pieces", primaryMetricFraction: 0.94f,
                    secondaryMetricLabel: "8% of mass", secondaryMetricFraction: 0.08f),
                new InfoCard("A closer look",
                    "These tiny fragments are enlarged for visibility. Each exhibit band organizes a size category; its thickness represents estimated mass share.",
                    "Exhibit visualization")
            },
            new[]
            {
                new InfoCard("Mesoplastic", "Small pieces from 5 to 50 mm can include broken plastic and bottle caps.", SizeStudy),
                new InfoCard("Small pieces, lasting impacts",
                    "Plastic breaks into smaller fragments. Marine animals can mistake plastic for food; swallowed debris can cause injury or block digestion.",
                    NoaaMarineDebris),
                new InfoCard("A closer look",
                    "Models show examples of small plastic pieces and are enlarged for visibility. Exhibit bands organize size categories, not ocean depths.",
                    "Exhibit visualization")
            },
            new[]
            {
                new InfoCard("Macroplastic", "Pieces from 5 to 50 cm can include bottles, containers, and broken objects.", SizeStudy),
                new InfoCard("A date on a buoy",
                    "A buoy collected in 2019 carried a production date of 1966. This was a research specimen; the displayed model is illustrative.",
                    CountrySource, "1966"),
                new InfoCard("A closer look",
                    "The displayed object illustrates a size category. It does not identify a specific object's origin or age. Band thickness represents estimated mass share.",
                    "Exhibit visualization")
            },
            new[]
            {
                new InfoCard("Megaplastic", "Debris larger than 50 cm includes lost nets, ropes, and bulky plastic objects.", SizeStudy),
                new InfoCard("Fishing nets by mass",
                    "Fishing nets accounted for this share of estimated floating-plastic mass in the study area.",
                    SizeStudy, "At least 46%"),
                new InfoCard("A closer look",
                    "Models illustrate large debris and are scaled to fit the display. Band thickness represents estimated mass share, not ocean depth.",
                    "Exhibit visualization")
            }
        };

        static readonly InfoCard[] TireCards =
        {
            new InfoCard("Rubber debris",
                "Tires are bulky rubber litter. This illustrative model represents the >50 cm size group.",
                "Exhibit visualization"),
            new InfoCard("Surface detail",
                "Look at the tread, sidewall and weathered surface. The enlarged view makes these details easier to see.",
                "Illustrative model"),
            new InfoCard("Read the exhibit",
                "Band heights represent estimated plastic mass shares. The rubber tires provide additional marine-litter context.",
                "Exhibit visualization; " + SizeStudy)
        };

        public static InfoCard[] DebrisCards(int category, string modelName = null)
        {
            if (IsRubberDebris(modelName)) return TireCards;
            return CategoryCards[IsCategory(category) ? category : 0];
        }

        public static string CategoryName(int category)
        {
            return IsCategory(category) ? CategoryNames[category] : "Plastic debris";
        }

        public static string CategoryRange(int category)
        {
            return IsCategory(category) ? CategoryRanges[category] : "Size unknown";
        }

        public static string CategoryDescription(int category)
        {
            return IsCategory(category) ? CategoryDescriptions[category] :
                "Models and arrangement are illustrative.";
        }

        // Inferred origins of 232 identifiable rigid plastic objects >5 cm,
        // collected in 2019, from a collection of 6,048 plastic objects.
        // Counts concern that identifiable sample, not mass or whole-patch shares.
        // Markings and local brands suggest origin; they do not identify who
        // discarded an object or the place where it entered the ocean.
        // https://www.nature.com/articles/s41598-022-16529-0
        // Exact counts and one-decimal percentages from the publisher:
        // https://www.natureasia.com/ja-jp/earth-env/research/14202
        public const string CountryScope =
            "232 identifiable hard-plastic objects >5 cm, collected in 2019. Shares by count within this sample.";
        public const string CountrySource =
            "Lebreton et al., Scientific Reports (2022)";

        public static readonly string[] CountryNames =
        {
            "Japan", "China", "Korea", "USA", "Taiwan", "Canada"
        };

        public static readonly int[] CountryCounts = { 78, 75, 23, 15, 13, 11 };
        public static readonly float[] CountryPercentages = { 33.6f, 32.3f, 9.9f, 6.5f, 5.6f, 4.7f };

        /// <summary>Names for the existing prepared and raw model pools.</summary>
        public static string FriendlyModelName(string rawName)
        {
            string name = CleanModelName(rawName);
            string compact = Compact(name);
            if (compact.Contains("bottlecap")) return "Bottle cap";
            if (compact.Contains("microplastic") && IsFragment(compact)) return "Microplastic fragment";
            if (compact.Contains("mesoplastic") && IsFragment(compact)) return "Mesoplastic fragment";
            if (compact.Contains("canister")) return "Plastic canister";
            if (compact.Contains("gardenchair")) return "Plastic garden chair";
            if (compact.Contains("trucktire")) return "Truck tire";
            if (compact.Contains("tirepile")) return "Tire pile";
            if (compact.Contains("jerrycan")) return "Plastic jerrycan";
            if (compact.Contains("lifebuoy")) return "Lifebuoy";
            if (compact.Contains("crate")) return "Plastic crate";
            if (compact.Contains("barrel")) return "Plastic barrel";
            if (compact.Contains("bucket")) return "Plastic bucket";
            if (compact.Contains("bottle")) return "Plastic bottle";
            if (compact.Contains("net")) return "Fishing net";
            if (compact.Contains("rope")) return "Rope";
            if (IsFragment(compact)) return "Plastic fragment";
            if (compact.Contains("seaturtle")) return "Sea turtle";
            if (string.IsNullOrEmpty(name) || compact == "model" || compact == "mesh" || compact == "default")
                return "Plastic debris";

            name = Regex.Replace(name, "[_-]+", " ");
            name = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            name = Regex.Replace(name, @"\s+", " ").Trim();
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        }

        /// <summary>
        /// Allows contextual sample figures for recognized large rigid plastics.
        /// This never attributes the selected illustrative model to a country.
        /// </summary>
        public static bool EligibleCountryContext(int category, string modelName)
        {
            if (category != 2 && category != 3) return false;
            if (IsRubberDebris(modelName)) return false;
            string compact = Compact(CleanModelName(modelName));
            if (compact.Contains("net") || compact.Contains("rope") || compact.Contains("lifebuoy"))
                return false;
            return compact.Contains("crate") || compact.Contains("jerrycan") ||
                compact.Contains("canister") || compact.Contains("gardenchair") ||
                compact.Contains("barrel") || compact.Contains("bucket") ||
                compact.Contains("bottle") || IsFragment(compact);
        }

        /// <summary>Keeps illustrative rubber tires separate from plastic study figures.</summary>
        public static bool IsRubberDebris(string modelName)
        {
            string compact = Compact(CleanModelName(modelName));
            return compact.Contains("trucktire") || compact.Contains("tirepile");
        }

        static bool IsCategory(int category)
        {
            return category >= 0 && category < CategoryNames.Length;
        }

        static bool IsFragment(string compact)
        {
            return compact.Contains("shard") || compact.Contains("fragment");
        }

        static string Compact(string name)
        {
            return Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]", string.Empty);
        }

        static string CleanModelName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;
            string name = rawName.Trim().Replace('\\', '/');
            int slash = name.LastIndexOf('/');
            if (slash >= 0) name = name.Substring(slash + 1);
            name = Regex.Replace(name, @"\s*\((clone|copy|\d+)\)", string.Empty,
                RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\.(fbx|obj|prefab|glb|gltf)$", string.Empty,
                RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s+\d{3}$", string.Empty);

            // Imported copies may combine several prefixes, for example
            // "Copy of Prepared Plastic_Crate" or "Matte_Plastic_Bucket".
            string previous;
            do
            {
                previous = name;
                name = Regex.Replace(name,
                    @"^(copy(?:\s+of)?|clone(?:\s+of)?|prepared|matte)[\s_:\-]+",
                    string.Empty, RegexOptions.IgnoreCase);
            } while (!string.Equals(name, previous, StringComparison.Ordinal));

            name = Regex.Replace(name, @"[_\s-]+[0-9a-f]{16,}$", string.Empty,
                RegexOptions.IgnoreCase);
            return name.Trim();
        }
    }
}
