namespace ResearchOrganized.Layout
{
    /// <summary>Dependency-free tab routing policy, separated from RimWorld def lookup for tests.</summary>
    public static class TabRoutingPolicy
    {
        public sealed class Request
        {
            public string CurrentTab;
            public string AnomalyTab;
            public string OverrideTab;
            public string DefaultTab;
            public string IndustrialTab;
            public string HighIndustrialTab;
            public string LateIndustrialTab;
            public bool IsAnomaly;
            public bool CurrentTabIgnored;
            public bool CurrentTabPreserved;
            public bool IsIndustrial;
            public bool CombineIndustrial;
            public bool RequiresHighTechBench;
            public bool RequiresMultiAnalyzer;
        }

        public static string Resolve(Request request)
        {
            if (request.IsAnomaly && request.AnomalyTab != null) return request.AnomalyTab;
            if (request.OverrideTab != null) return request.OverrideTab;
            if (request.CurrentTabIgnored || request.CurrentTabPreserved) return request.CurrentTab;

            if (request.IsIndustrial)
            {
                if (request.CombineIndustrial) return request.IndustrialTab ?? request.CurrentTab;
                if (request.RequiresMultiAnalyzer && request.LateIndustrialTab != null) return request.LateIndustrialTab;
                if (request.RequiresHighTechBench && request.HighIndustrialTab != null) return request.HighIndustrialTab;
                return request.IndustrialTab ?? request.CurrentTab;
            }

            return request.DefaultTab ?? request.CurrentTab;
        }
    }
}
