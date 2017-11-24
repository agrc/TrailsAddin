using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using System.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace TrailsAddin
{
    internal class Main : Module
    {
        private static Main _this = null;
        public static FeatureLayer SGIDTrailheadsLayer;
        public static FeatureLayer SGIDTrailsLayer;
        public static FeatureLayer SegmentsLayer;
        public static StandaloneTable RoutesStandaloneTable;

        /// <summary>
        /// Retrieve the singleton instance to this module here
        /// </summary>
        public static Main Current
        {
            get
            {
                return _this ?? (_this = (Main)FrameworkApplication.FindModule("TrailsAddin_Module"));
            }
        }

        static Main()
        {
            // get layer references
            SGIDTrailheadsLayer = GetLayer("SGID10.RECREATION.Trailheads");
            SGIDTrailsLayer = GetLayer("SGID10.RECREATION.Trails");
            SegmentsLayer = GetLayer("TrailSegments");
            RoutesStandaloneTable = MapView.Active.Map.StandaloneTables.First(l => l.Name == "Routes") as StandaloneTable;
        }

        private static FeatureLayer GetLayer(string name)
        {
            return MapView.Active.Map.GetLayersAsFlattenedList().First(l => l.Name == name) as FeatureLayer;
        }

        #region Overrides
        /// <summary>
        /// Called by Framework when ArcGIS Pro is closing
        /// </summary>
        /// <returns>False to prevent Pro from closing, otherwise True</returns>
        protected override bool CanUnload()
        {
            //TODO - add your business logic
            //return false to ~cancel~ Application close
            return true;
        }

        #endregion Overrides

    }
}
