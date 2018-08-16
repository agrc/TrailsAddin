using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace TrailsAddin.Views
{
    internal class EvergreenSettingsViewModel : Page
    {
        /// <summary>
        /// Invoked when the OK or apply button on the property sheet has been clicked.
        /// </summary>
        /// <returns>A task that represents the work queued to execute in the ThreadPool.</returns>
        /// <remarks>This function is only called if the page has set its IsModified flag to true.</remarks>
        protected override Task CommitAsync()
        {
            return Task.FromResult(0);
        }

        /// <summary>
        /// Called when the page loads because to has become visible.
        /// </summary>
        /// <returns>A task that represents the work queued to execute in the ThreadPool.</returns>
        protected override Task InitializeAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// Called when the page is destroyed.
        /// </summary>
        protected override void Uninitialize()
        {
        }

        /// <summary>
        /// Text shown inside the page hosted by the property sheet
        /// </summary>
        public string DataUIContent
        {
            get
            {
                return base.Data[0] as string;
            }
            set
            {
                SetProperty(ref base.Data[0], value, () => DataUIContent);
            }
        }
    }

    /// <summary>
    /// Button implementation to show the property sheet.
    /// </summary>
    internal class EvergreenSettings_ShowButton : Button
    {
        protected override void OnClick()
        {
            // collect data to be passed to the page(s) of the property sheet
            Object[] data = new object[] { "Page UI content" };

            if (!PropertySheet.IsVisible)
                PropertySheet.ShowDialog("TrailsAddin_Views_EvergreenSettings", "TrailsAddin_Views_EvergreenSettings", data);

        }
    }
}
