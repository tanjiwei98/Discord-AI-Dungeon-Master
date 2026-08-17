using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace AIDungeonMaster.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly CancellationTokenSource _botCancellation = new();

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainWindowViewModel(
            new SecureConfigurationService(),
            new DesktopBotService(
                new DiscordConnectionService(),
                new OpenAIConnectionService()));

        DataContext = _viewModel;

        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            SyncUiToViewModel();
            LogTextBox.Text = _viewModel.LogText;
        };

        Closing += (_, _) =>
        {
            _botCancellation.Cancel();
        };
    }

    private void SyncUiToViewModel()
    {
        DiscordTokenBox.Password = _viewModel.DiscordBotToken;
        OpenAiKeyBox.Password = _viewModel.OpenAIApiKey;
    }

    private void DiscordTokenBox_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.DiscordBotToken =
            DiscordTokenBox.Password;
    }

    private void OpenAiKeyBox_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        _viewModel.OpenAIApiKey =
            OpenAiKeyBox.Password;
    }

    private async void TestConfigurationButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;
            try
            {
                await _viewModel.TestConfigurationAsync();
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        LogTextBox.Text = _viewModel.LogText;
        LogTextBox.ScrollToEnd();
    }

    private async void StartButton_OnClick(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.IsEnabled = false;
            try
            {
                await _viewModel.StartAsync(
                    _botCancellation.Token);
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        LogTextBox.Text = _viewModel.LogText;
        LogTextBox.ScrollToEnd();
    }
}

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IConfigurationService _configurationService;
    private readonly IBotService _botService;
    private string _discordBotToken = string.Empty;
    private string _openAIApiKey = string.Empty;
    private string _selectedLanguage = "English";
    private string _openAIModel = "gpt-5.4-mini";
    private string _discordStatus = "Not tested yet.";
    private string _openAIStatus = "Not tested yet.";
    private string _errorMessage = string.Empty;
    private string _logText = string.Empty;
    private bool _isBusy;

    public MainWindowViewModel(
        IConfigurationService configurationService,
        IBotService botService)
    {
        _configurationService = configurationService;
        _botService = botService;

        Languages = new ObservableCollection<string>
        {
            "English",
            "Chinese"
        };

        ModelPresets = new ObservableCollection<string>
        {
            "gpt-5.4-mini",
            "gpt-4.1-mini",
            "gpt-4.1",
            "gpt-5.4"
        };
    }

    public ObservableCollection<string> Languages { get; }

    public ObservableCollection<string> ModelPresets { get; }

    public string DiscordBotToken
    {
        get => _discordBotToken;
        set => SetField(ref _discordBotToken, value);
    }

    public string OpenAIApiKey
    {
        get => _openAIApiKey;
        set => SetField(ref _openAIApiKey, value);
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetField(ref _selectedLanguage, value);
    }

    public string OpenAIModel
    {
        get => _openAIModel;
        set => SetField(ref _openAIModel, value);
    }

    public string DiscordStatus
    {
        get => _discordStatus;
        private set => SetField(ref _discordStatus, value);
    }

    public string OpenAIStatus
    {
        get => _openAIStatus;
        private set => SetField(ref _openAIStatus, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetField(ref _logText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public async Task InitializeAsync()
    {
        var configuration =
            await _configurationService.LoadAsync();

        DiscordBotToken = configuration.DiscordBotToken;
        OpenAIApiKey = configuration.OpenAIApiKey;
        SelectedLanguage =
            string.IsNullOrWhiteSpace(configuration.DefaultLanguage)
                ? "English"
                : configuration.DefaultLanguage;
        OpenAIModel =
            string.IsNullOrWhiteSpace(configuration.OpenAIModel)
                ? "gpt-5.4-mini"
                : configuration.OpenAIModel;

        AppendLog("Loaded local configuration.");
    }

    public async Task TestConfigurationAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;

        try
        {
            var configuration =
                BuildConfiguration();

            var issues =
                BotConfigurationValidator.Validate(configuration);

            if (issues.Count > 0)
            {
                ErrorMessage =
                    string.Join(
                        Environment.NewLine,
                        issues.Select(issue => issue.Message));

                DiscordStatus = "Not tested.";
                OpenAIStatus = "Not tested.";

                return;
            }

            var testResult =
                await _botService.TestConfigurationAsync(
                    configuration,
                    CancellationToken.None);

            DiscordStatus =
                testResult.Statuses.FirstOrDefault(
                    x => x.Service == "Discord")
                ?.Message ?? "Not tested.";

            OpenAIStatus =
                testResult.Statuses.FirstOrDefault(
                    x => x.Service == "OpenAI")
                ?.Message ?? "Not tested.";

            if (testResult.Issues.Count > 0)
            {
                ErrorMessage =
                    string.Join(
                        Environment.NewLine,
                        testResult.Issues.Select(issue => issue.Message));
            }
            else
            {
                ErrorMessage = string.Empty;
                AppendLog("Configuration test completed.");
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppendLog("Configuration test failed.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task StartAsync(
        CancellationToken cancellationToken)
    {
        ErrorMessage = string.Empty;
        IsBusy = true;

        try
        {
            var configuration =
                BuildConfiguration();

            var issues =
                BotConfigurationValidator.Validate(configuration);

            if (issues.Count > 0)
            {
                ErrorMessage =
                    string.Join(
                        Environment.NewLine,
                        issues.Select(issue => issue.Message));
                return;
            }

            await _configurationService.SaveAsync(configuration);

            AppendLog("Saved local configuration.");
            AppendLog("Starting AI Dungeon Master...");

            var progress =
                new Progress<string>(AppendLog);

            await _botService.StartAsync(
                configuration,
                progress,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Startup cancelled.");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            AppendLog("Failed to start the bot.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private BotConfiguration BuildConfiguration()
    {
        return new BotConfiguration
        {
            DiscordBotToken = DiscordBotToken.Trim(),
            OpenAIApiKey = OpenAIApiKey.Trim(),
            DefaultLanguage = SelectedLanguage.Trim(),
            OpenAIModel = OpenAIModel.Trim(),
            ChoiceTimeoutMinutes = string.Empty
        };
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        LogText +=
            $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
    }
}
