﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Java.Util;
using BrickController2.PlatformServices.BluetoothLE;

namespace BrickController2.Droid.PlatformServices.BluetoothLE
{
    public class BluetoothLEDevice : BluetoothGattCallback, IBluetoothLEDevice
    {
        private static readonly UUID? ClientCharacteristicConfigurationUUID = UUID.FromString("00002902-0000-1000-8000-00805f9b34fb");

        private readonly Context _context;
        private readonly BluetoothAdapter _bluetoothAdapter;
        private readonly object _lock = new object();

        private BluetoothDevice? _bluetoothDevice = null;
        private BluetoothGatt? _bluetoothGatt = null;

        private TaskCompletionSource<IEnumerable<IGattService>?>? _connectCompletionSource = null;
        private TaskCompletionSource<byte[]?>? _readCompletionSource = null;
        private TaskCompletionSource<bool>? _writeCompletionSource = null;
        private TaskCompletionSource<bool>? _descriptorWriteCompletionSource = null;

        private Action<Guid, byte[]>? _onCharacteristicChanged = null;
        private Action<IBluetoothLEDevice>? _onDeviceDisconnected = null;

        public BluetoothLEDevice(Context context, BluetoothAdapter bluetoothAdapter, string address)
        {
            _context = context;
            _bluetoothAdapter = bluetoothAdapter;
            Address = address;
        }

        public string Address { get; }
        public BluetoothLEDeviceState State { get; private set; } = BluetoothLEDeviceState.Disconnected;

        public async Task<IEnumerable<IGattService>?> ConnectAndDiscoverServicesAsync(
            bool autoConnect,
            Action<Guid, byte[]> onCharacteristicChanged,
            Action<IBluetoothLEDevice> onDeviceDisconnected,
            CancellationToken token)
        {
            using (token.Register(() =>
            {
                lock (_lock)
                {
                    Disconnect();
                    _connectCompletionSource?.TrySetResult(null);
                }
            }))
            {
                lock (_lock)
                {
                    if (State != BluetoothLEDeviceState.Disconnected)
                    {
                        return null;
                    }

                    _onCharacteristicChanged = onCharacteristicChanged;
                    _onDeviceDisconnected = onDeviceDisconnected;

                    State = BluetoothLEDeviceState.Connecting;

                    _bluetoothDevice = _bluetoothAdapter.GetRemoteDevice(Address);
                    if (_bluetoothDevice is null)
                    {
                        State = BluetoothLEDeviceState.Disconnected;
                        return null;
                    }

                    if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
                    {
                        _bluetoothGatt = _bluetoothDevice.ConnectGatt(_context, autoConnect, this, BluetoothTransports.Le);
                    }
                    else
                    {
                        _bluetoothGatt = _bluetoothDevice.ConnectGatt(_context, autoConnect, this);
                    }

                    if (_bluetoothGatt is null)
                    {
                        _bluetoothDevice.Dispose();
                        _bluetoothDevice = null;
                        State = BluetoothLEDeviceState.Disconnected;
                        return null;
                    }

                    _connectCompletionSource = new TaskCompletionSource<IEnumerable<IGattService>?>(TaskCreationOptions.RunContinuationsAsynchronously);
                }

                var result = await _connectCompletionSource.Task;

                lock (_lock)
                {
                    _connectCompletionSource = null;
                    return result;
                }
            }
        }

        internal void Disconnect()
        {

            _onDeviceDisconnected = null;
            _onCharacteristicChanged = null;

            if (_bluetoothGatt != null)
            {
                _bluetoothGatt.Disconnect();
                _bluetoothGatt.Close();
                _bluetoothGatt.Dispose();
                _bluetoothGatt = null;

                _bluetoothDevice?.Dispose();
                _bluetoothDevice = null;
            }

            State = BluetoothLEDeviceState.Disconnected;
        }

        public Task DisconnectAsync()
        {
            lock (_lock)
            {
                Disconnect();
            }

            return Task.CompletedTask;
        }

        public async Task<bool> EnableNotificationAsync(IGattCharacteristic characteristic, CancellationToken token)
        {
            using (token.Register(() =>
            {
                lock (_lock)
                {
                    _descriptorWriteCompletionSource?.TrySetResult(false);
                }
            }))

            lock (_lock)
            {
                if (_bluetoothGatt == null || State != BluetoothLEDeviceState.Connected)
                {
                    return false;
                }

                var nativeCharacteristic = ((GattCharacteristic)characteristic).BluetoothGattCharacteristic;
                if (!_bluetoothGatt.SetCharacteristicNotification(nativeCharacteristic, true))
                {
                    return false;
                }

                var descriptor = nativeCharacteristic.GetDescriptor(ClientCharacteristicConfigurationUUID);
                if (descriptor == null)
                {
                    return false;
                }

#pragma warning disable CA1422 // Validate platform compatibility
                    if (!(descriptor?.SetValue(BluetoothGattDescriptor.EnableNotificationValue!.ToArray()) ?? false))
                {
                    return false;
                }
#pragma warning restore CA1422 // Validate platform compatibility

                    _descriptorWriteCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable CA1422 // Validate platform compatibility
                    if (!(_bluetoothGatt?.WriteDescriptor(descriptor) ?? false))
                {
                    _descriptorWriteCompletionSource = null;
                    return false;
                }
#pragma warning restore CA1422 // Validate platform compatibility
                }

            var result = await _descriptorWriteCompletionSource.Task.ConfigureAwait(false);

            lock (_lock)
            {
                _descriptorWriteCompletionSource = null;
                return result;
            }
        }

        public async Task<byte[]?> ReadAsync(IGattCharacteristic characteristic, CancellationToken token)
        {
            using (token.Register(() =>
            {
                lock (_lock)
                {
                    _readCompletionSource?.TrySetResult(null);
                }
            }))
            {
                lock (_lock)
                {
                    var nativeCharacteristic = ((GattCharacteristic)characteristic).BluetoothGattCharacteristic;

                    _readCompletionSource = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);

                    if (!(_bluetoothGatt?.ReadCharacteristic(nativeCharacteristic) ?? false))
                    {
                        _readCompletionSource = null;
                        return null;
                    }
                }

                var result = await _readCompletionSource.Task;

                lock (_lock)
                {
                    _readCompletionSource = null;
                    return result;
                }
            }
        }

        public async Task<bool> WriteAsync(IGattCharacteristic characteristic, byte[] data, CancellationToken token)
        {
            using (token.Register(() =>
            {
                lock (_lock)
                {
                    _writeCompletionSource?.TrySetResult(false);
                }
            }))
            {
                lock (_lock)
                {
                    if (_bluetoothGatt == null || State != BluetoothLEDeviceState.Connected)
                    {
                        return false;
                    }

                    var nativeCharacteristic = ((GattCharacteristic)characteristic).BluetoothGattCharacteristic;
                    nativeCharacteristic.WriteType = GattWriteType.Default;

#pragma warning disable CA1422 // Validate platform compatibility
                    if (!(nativeCharacteristic?.SetValue(data) ?? false))
                    {
                        return false;
                    }
#pragma warning restore CA1422 // Validate platform compatibility

                    _writeCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

#pragma warning disable CA1422 // Validate platform compatibility
                    if (!(_bluetoothGatt?.WriteCharacteristic(nativeCharacteristic) ?? false))
                    {
                        _writeCompletionSource = null;
                        return false;
                    }
#pragma warning restore CA1422 // Validate platform compatibility
                }

                var result = await _writeCompletionSource.Task.ConfigureAwait(false);

                lock (_lock)
                {
                    _writeCompletionSource = null;
                    return result;
                }
            }
        }

        public Task<bool> WriteNoResponseAsync(IGattCharacteristic characteristic, byte[] data, CancellationToken token)
        {
            lock (_lock)
            {
                if (_bluetoothGatt == null || State != BluetoothLEDeviceState.Connected)
                {
                    return Task.FromResult(false);
                }

                var nativeCharacteristic = ((GattCharacteristic)characteristic).BluetoothGattCharacteristic;
                nativeCharacteristic.WriteType = GattWriteType.NoResponse;

#pragma warning disable CA1422 // Validate platform compatibility
                if (!(nativeCharacteristic?.SetValue(data) ?? false))
                {
                    return Task.FromResult(false);
                }
#pragma warning restore CA1422 // Validate platform compatibility

#pragma warning disable CA1422 // Validate platform compatibility
                var result = _bluetoothGatt?.WriteCharacteristic(nativeCharacteristic) ?? false;
#pragma warning restore CA1422 // Validate platform compatibility
                return Task.FromResult(result);
            }
        }

        public override void OnConnectionStateChange(BluetoothGatt? gatt, [GeneratedEnum] GattStatus status, [GeneratedEnum] ProfileState newState)
        {
            switch (newState)
            {
                case ProfileState.Connecting:
                    break;

                case ProfileState.Connected:
                    lock (_lock)
                    {
                        if (State == BluetoothLEDeviceState.Connecting && status == GattStatus.Success)
                        {
                            State = BluetoothLEDeviceState.Discovering;
                            Task.Run(async () =>
                            {
                                await Task.Delay(750);
                                lock (_lock)
                                {
                                    if (State == BluetoothLEDeviceState.Discovering && _bluetoothGatt != null)
                                    {
                                        if (!_bluetoothGatt.DiscoverServices())
                                        {
                                            Disconnect();
                                            _connectCompletionSource?.TrySetResult(null);
                                        }
                                    }
                                }
                            });
                        }
                        else
                        {
                            Disconnect();
                            _connectCompletionSource?.TrySetResult(null);
                        }
                    }

                    break;

                case ProfileState.Disconnecting:
                    break;

                case ProfileState.Disconnected:
                    lock (_lock)
                    {
                        switch (State)
                        {
                            case BluetoothLEDeviceState.Connecting:
                            case BluetoothLEDeviceState.Discovering:
                                Disconnect();
                                _connectCompletionSource?.TrySetResult(null);
                                break;

                            case BluetoothLEDeviceState.Connected:
                                _writeCompletionSource?.TrySetResult(false);

                                // Copy the _onDeviceDisconnected callback to call it
                                // in case of an unexpected disconnection
                                var onDeviceDisconnected = _onDeviceDisconnected;

                                Disconnect();
                                onDeviceDisconnected?.Invoke(this);
                                break;

                            default:
                                break;
                        }
                    }

                    break;
            }
        }

        public override void OnServicesDiscovered(BluetoothGatt? gatt, [GeneratedEnum] GattStatus status)
        {
            lock (_lock)
            {
                if (status == GattStatus.Success && State == BluetoothLEDeviceState.Discovering)
                {
                    var services = new List<GattService>();
                    if (gatt?.Services is not null)
                    {
                        foreach (var service in gatt.Services)
                        {
                            var characteristics = new List<GattCharacteristic>();
                            if (service.Characteristics != null)
                            {
                                foreach (var characteristic in service.Characteristics)
                                {
                                    characteristics.Add(new GattCharacteristic(characteristic));
                                }
                            }

                            services.Add(new GattService(service, characteristics));
                        }
                    }

                    State = BluetoothLEDeviceState.Connected;
                    _connectCompletionSource?.TrySetResult(services);
                }
                else
                {
                    Disconnect();
                    _connectCompletionSource?.TrySetResult(null);
                }
            }
        }

        public override void OnCharacteristicRead(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, [GeneratedEnum] GattStatus status)
        {
            lock (_lock)
            {
#pragma warning disable CA1422 // Validate platform compatibility
                _readCompletionSource?.TrySetResult(characteristic?.GetValue());
#pragma warning restore CA1422 // Validate platform compatibility
            }
        }

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, [GeneratedEnum] GattStatus status)
        {
            lock (_lock)
            {
                _writeCompletionSource?.TrySetResult(status == GattStatus.Success);
            }
        }

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, [GeneratedEnum] GattStatus status)
        {
            lock (_lock)
            {
                _descriptorWriteCompletionSource?.TrySetResult(status == GattStatus.Success);
            }
        }

        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            lock (_lock)
            {
                var guid = characteristic?.Uuid?.ToGuid();
#pragma warning disable CA1422 // Validate platform compatibility
                var data = characteristic?.GetValue();
#pragma warning restore CA1422 // Validate platform compatibility

                if (guid is not null && data is not null)
                {
                    _onCharacteristicChanged?.Invoke(guid.Value, data);
                }
            }
        }
    }
}