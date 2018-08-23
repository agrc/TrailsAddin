using System.Collections.Generic;
using System.Linq;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Mapping;

namespace TrailsAddin
{
    /// <summary>
    /// Represents the ComboBox
    /// </summary>
    internal class ReviewComboBox : ComboBox
    {

        private bool _isInitialized;
        private static Main m = Main.Current;
        private static string tablePrefix = "UtahTrails.TRAILSADMIN.";
        private Dictionary<string, object[]> checks = new Dictionary<string, object[]>()
        {
            { "Duplicate Segment IDs", new object[] { m.SegmentsLayer, $"{m.USNG_SEG} IS NOT NULL AND {m.USNG_SEG} IN (SELECT {m.USNG_SEG} FROM {tablePrefix}{m.TrailSegments} GROUP BY {m.USNG_SEG} HAVING COUNT(*) > 1)"} },
            { "Duplicate Trailhead IDs", new object[] { m.HeadsLayer, $"{m.USNG_TH} IS NOT NULL AND {m.USNG_TH} IN (SELECT {m.USNG_TH} FROM {tablePrefix}{m.Trailheads} GROUP BY {m.USNG_TH} HAVING COUNT(*) > 1)"} },
            { "RouteToTrailheads - Missing Routes", new object[] { m.RouteToTrailheadsTable, $"{m.RouteID} NOT IN (SELECT {m.RouteID} FROM {tablePrefix}{m.Routes})" } },
            { "RouteToTrailheads - Missing Trailheads", new object[] { m.RouteToTrailheadsTable, $"{m.USNG_TH} NOT IN (SELECT {m.USNG_TH} FROM {tablePrefix}{m.Trailheads})" } },
            { "RouteToTrailSegments - Missing Routes", new object[] { m.RouteToTrailSegmentsTable, $"{m.RouteID} NOT IN (SELECT {m.RouteID} FROM {tablePrefix}{m.Routes})" } },
            { "RouteToTrailSegments - Missing Segments", new object[] { m.RouteToTrailSegmentsTable, $"{m.USNG_SEG} NOT IN (SELECT {m.USNG_SEG} FROM {tablePrefix}{m.TrailSegments})" } }
        };
        private string placeholder = "select an option";

        /// <summary>
        /// Combo Box constructor
        /// </summary>
        public ReviewComboBox()
        {
            AddItems();
        }

        /// <summary>
        /// Updates the combo box with all the items.
        /// </summary>

        private void AddItems()
        {
            if (_isInitialized)
                SelectedItem = ItemCollection.FirstOrDefault(); //set the default item in the comboBox

            if (!_isInitialized)
            {
                Clear();

                Add(new ComboBoxItem(placeholder));
                foreach (var item in checks)
                {
                    Add(new ComboBoxItem(item.Key, null, item.Value[1] as string));
                }
                _isInitialized = true;
            }

            Enabled = true; //enables the ComboBox
            SelectedItem = ItemCollection.FirstOrDefault(); //set the default item in the comboBox
        }

        /// <summary>
        /// The on comboBox selection change event. 
        /// </summary>
        /// <param name="item">The newly selected combo box item</param>
        protected override void OnSelectionChange(ComboBoxItem item)
        {

            if (item == null || string.IsNullOrEmpty(item.Text) || item.Text == placeholder)
                return;

            QueuedTask.Run(() =>
            {
                var checkItem = checks[item.Text];
                var displayTable = checkItem[0] as IDisplayTable;
                displayTable.Select(new QueryFilter()
                {
                    WhereClause = checkItem[1] as string
                });
            });
        }

    }
}
