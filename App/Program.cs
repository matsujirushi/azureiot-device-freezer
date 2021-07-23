using CommandLine;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Samples;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Freezer
{
    class Program
    {
        private const string ModelId = "dtmi:com:example:Thermostat;1";

        static async Task Main(string[] args)
        {
            Parameters parameters = null;
            ParserResult<Parameters> result = Parser.Default.ParseArguments<Parameters>(args)
                .WithParsed(parsedParams =>
                {
                    parameters = parsedParams;
                })
                .WithNotParsed(errors =>
                {
                    Environment.Exit(1);
                });

            if (!parameters.Validate())
            {
                throw new ArgumentException("Required parameters are not set. Please recheck required variables by using \"--help\"");
            }

            Console.WriteLine("Press Control+C to quit the freezer.");
            using var cts = new CancellationTokenSource(Timeout.InfiniteTimeSpan);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Freezer execution cancellation requested; will exit.");
            };

            Console.WriteLine("Set up the device client.");
            DeviceRegistrationResult dpsRegistrationResult = await ProvisionDeviceAsync(parameters, cts.Token);
            var authMethod = new DeviceAuthenticationWithRegistrySymmetricKey(dpsRegistrationResult.DeviceId, parameters.DeviceSymmetricKey);
            var deviceClient = InitializeDeviceClient(dpsRegistrationResult.AssignedHub, authMethod);

            while (!cts.Token.IsCancellationRequested)
            {
                await SendTemperatureTelemetryAsync(deviceClient, 20.0);
                await Task.Delay(5 * 1000);
            }

            await deviceClient.CloseAsync();
        }

        private static async Task<DeviceRegistrationResult> ProvisionDeviceAsync(Parameters parameters, CancellationToken cancellationToken)
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

        private static DeviceClient InitializeDeviceClient(string hostname, IAuthenticationMethod authenticationMethod)
        {
            var options = new ClientOptions
            {
                ModelId = ModelId,
            };

            var deviceClient = DeviceClient.Create(hostname, authenticationMethod, TransportType.Mqtt, options);
            deviceClient.SetConnectionStatusChangesHandler((status, reason) =>
            {
                Console.WriteLine($"Connection status change registered - status={status}, reason={reason}.");
            });

            return deviceClient;
        }

        private static async Task SendTemperatureTelemetryAsync(DeviceClient _deviceClient, double _temperature)
        {
            const string telemetryName = "temperature";

            string telemetryPayload = $"{{ \"{telemetryName}\": {_temperature} }}";
            using var message = new Message(Encoding.UTF8.GetBytes(telemetryPayload))
            {
                ContentEncoding = "utf-8",
                ContentType = "application/json",
            };

            await _deviceClient.SendEventAsync(message);
            Console.WriteLine($"Telemetry: Sent - {{ \"{telemetryName}\": {_temperature}°C }}.");
        }

    }
}
