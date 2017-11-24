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
    internal class TrailsModule : Module
    {
        private static TrailsModule _this = null;
        public string NewRouteName;

        /// <summary>
        /// Retrieve the singleton instance to this module here
        /// </summary>
        public static TrailsModule Current
        {
            get
            {
                return _this ?? (_this = (TrailsModule)FrameworkApplication.FindModule("TrailsAddin_Module"));
            }
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
