using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Samples;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Freezer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string ModelId = "dtmi:com:example:Thermostat;1";

        private enum DeviceState
        {
            Disconnected,
            Connected,
        }

        private DeviceState State_;
        private Parameters Parameters_;
        private DeviceClient Client_;
        private DispatcherTimer Timer_;

        private void ChangeDeviceState(DeviceState state)
        {
            switch (state)
            {
                case DeviceState.Disconnected:
                    btnConnect.IsEnabled = true;
                    btnDisconnect.IsEnabled = false;
                    txtTelemetryInterval.IsEnabled = true;
                    txtTemperatureLow.IsEnabled = true;
                    txtTemperatureHigh.IsEnabled = true;
                    break;
                case DeviceState.Connected:
                    btnConnect.IsEnabled = false;
                    btnDisconnect.IsEnabled = true;
                    txtTelemetryInterval.IsEnabled = false;
                    txtTemperatureLow.IsEnabled = false;
                    txtTemperatureHigh.IsEnabled = false;
                    break;
            }

            State_ = state;
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Parameters_ = new Parameters();
            if (!Parameters_.Validate())
            {
                throw new ArgumentException("Required parameters are not set. Please recheck required variables by using \"--help\"");
            }

            Title = $"Freezer - {Parameters_.DeviceId}";

            Timer_ = new DispatcherTimer();
            Timer_.Interval = new TimeSpan(int.Parse(txtTelemetryInterval.Text) * (long)Math.Pow(10, 7));
            Timer_.Tick += Timer_Tick;

            ChangeDeviceState(DeviceState.Disconnected);
        }

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            Cursor = Cursors.Wait;
            try
            {
                using var cts = new CancellationTokenSource(Timeout.InfiniteTimeSpan);

                Debug.WriteLine("Set up the device client.");
                DeviceRegistrationResult dpsRegistrationResult = await ProvisionDeviceAsync(Parameters_, cts.Token);
                var authMethod = new DeviceAuthenticationWithRegistrySymmetricKey(dpsRegistrationResult.DeviceId, Parameters_.DeviceSymmetricKey);
                Client_ = InitializeDeviceClient(dpsRegistrationResult.AssignedHub, authMethod);

                Timer_.Start();

                ChangeDeviceState(DeviceState.Connected);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            Cursor = Cursors.Wait;
            try
            {
                Timer_.Stop();
                await Client_.CloseAsync();

                ChangeDeviceState(DeviceState.Disconnected);
            }
            finally
            {
                Cursor = null;
            }
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            var temperatureLow = double.Parse(txtTemperatureLow.Text);
            var temperatureHigh = double.Parse(txtTemperatureHigh.Text);
            var rand = new Random();
            var temperature = rand.NextDouble() * (temperatureHigh - temperatureLow) + temperatureLow;
            if (chkOverheat.IsChecked.Value) temperature += 100;

            Debug.WriteLine("Send telemetry.");
            await SendTemperatureTelemetryAsync(Client_, temperature);
        }

        private async Task<DeviceRegistrationResult> ProvisionDeviceAsync(Parameters parameters, CancellationToken cancellationToken)
        {
            SecurityProvider symmetricKeyProvider = new SecurityProviderSymmetricKey(parameters.DeviceId, parameters.DeviceSymmetricKey, null);
            ProvisioningTransportHandler mqttTransportHandler = new ProvisioningTransportHandlerMqtt();
            var pdc = ProvisioningDeviceClient.Create(parameters.DpsEndpoint, parameters.DpsIdScope, symmetricKeyProvider, mqttTransportHandler);

            var pnpPayload = new ProvisioningRegistrationAdditionalData
            {
                JsonData = $"{{ \"modelId\": \"{ModelId}\" }}",
            };

            return await pdc.RegisterAsync(pnpPayload, cancellationToken);
        }

        private DeviceClient InitializeDeviceClient(string hostname, IAuthenticationMethod authenticationMethod)
        {
            var options = new ClientOptions
            {
                ModelId = ModelId,
            };

            var deviceClient = DeviceClient.Create(hostname, authenticationMethod, TransportType.Mqtt, options);
            deviceClient.SetConnectionStatusChangesHandler((status, reason) =>
            {
                Debug.WriteLine($"Connection status change registered - status={status}, reason={reason}.");
            });

            return deviceClient;
        }

        private static async Task SendTemperatureTelemetryAsync(DeviceClient _deviceClient, double _temperature)
        {
            string telemetryPayload = $"{{ \"temperature\": {_temperature} }}";
            using var message = new Message(Encoding.UTF8.GetBytes(telemetryPayload))
            {
                ContentEncoding = "utf-8",
                ContentType = "application/json",
            };

            await _deviceClient.SendEventAsync(message);
            Debug.WriteLine($"Telemetry: Sent - {telemetryPayload}.");
        }

    }
}
