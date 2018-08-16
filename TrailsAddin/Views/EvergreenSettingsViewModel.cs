using System.Diagnostics;
using System.Threading.Tasks;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using Reactive.Bindings;

namespace TrailsAddin.Views
{
    internal class EvergreenSettingsViewModel : Page
    {
        private bool _betaChannel;

        public ReactiveCommand OpenRepository { get; set; } = new ReactiveCommand();

        public ReactiveProperty<string> CurrentVersion { get; }

        public bool BetaChannel
        {
            get => _betaChannel;
            set
            {
                var modified = value != _betaChannel;

                if (SetProperty(ref _betaChannel, value, () => BetaChannel) && modified)
                {
                    IsModified = true;
                }
            }
        }

        public EvergreenSettingsViewModel()
        {
            OpenRepository.Subscribe(() => Process.Start("https://github.com/agrc/TrailsAddin"));
        }

        /// <summary>
        ///     Text shown inside the page hosted by the property sheet
        /// </summary>
        public string DataUIContent
        {
            get => Data[0] as string;
            set { SetProperty(ref Data[0], value, () => DataUIContent); }
        }

        /// <summary>
        ///     Invoked when the OK or apply button on the property sheet has been clicked.
        /// </summary>
        /// <returns>A task that represents the work queued to execute in the ThreadPool.</returns>
        /// <remarks>This function is only called if the page has set its IsModified flag to true.</remarks>
        protected override Task CommitAsync()
        {
            return Task.FromResult(0);
        }

        /// <summary>
        ///     Called when the page loads because to has become visible.
        /// </summary>
        /// <returns>A task that represents the work queued to execute in the ThreadPool.</returns>
        protected override Task InitializeAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>
        ///     Called when the page is destroyed.
        /// </summary>
        protected override void Uninitialize()
        {
        }
    }
}
