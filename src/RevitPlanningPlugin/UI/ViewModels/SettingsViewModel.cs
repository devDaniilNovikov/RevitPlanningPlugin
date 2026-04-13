using System;
using System.Windows.Input;
using RevitPlanningPlugin.Infrastructure;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Configuration;

namespace RevitPlanningPlugin.UI.ViewModels
{
    public class SettingsViewModel : ObservableObject
    {
        private readonly PluginSettings _settings;
        private readonly Action _onSave;

        public SettingsViewModel(PluginSettings settings, Action onSave)
        {
            _settings = settings;
            _onSave = onSave;
            SaveCommand = new RelayCommand(Save);
        }

        public string BaseUrl
        {
            get => _settings.BaseUrl;
            set { _settings.BaseUrl = value; OnPropertyChanged(); }
        }

        public ApiEnvironment Environment
        {
            get => _settings.Environment;
            set { _settings.Environment = value; OnPropertyChanged(); }
        }

        public string ApiKey
        {
            get => _settings.ApiKey;
            set { _settings.ApiKey = value; OnPropertyChanged(); }
        }

        public string BearerToken
        {
            get => _settings.BearerToken;
            set { _settings.BearerToken = value; OnPropertyChanged(); }
        }

        public int RequestTimeout
        {
            get => _settings.RequestTimeoutSeconds;
            set { _settings.RequestTimeoutSeconds = value; OnPropertyChanged(); }
        }

        public int MaxRetries
        {
            get => _settings.MaxRetries;
            set { _settings.MaxRetries = value; OnPropertyChanged(); }
        }

        public Array Environments => Enum.GetValues(typeof(ApiEnvironment));

        public ICommand SaveCommand { get; }

        private void Save() => _onSave();
    }
}
