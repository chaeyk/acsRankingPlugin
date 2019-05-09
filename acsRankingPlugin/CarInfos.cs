using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
    class Car
    {
        public string CarName { get; private set; }
        public string DriverName { get; private set; }

        public Car(string carName, string driverName)
        {
            CarName = carName;
            DriverName = driverName;
        }
    }

    class CarInfos
    {
        private ACSClient _acsClient;

        private Dictionary<int, Car> _cars = new Dictionary<int, Car>();

        // 서버로 부터 car 정보가 오길 기다리는 waiter 객체들
        // (condition variable 처럼 쓰고 있다)
        private Dictionary<int, TaskCompletionSource<Car>> _getWaiters = new Dictionary<int, TaskCompletionSource<Car>>();

        private object _lock = new object();

        public CarInfos(ACSClient acsClient)
        {
            _acsClient = acsClient;

            _acsClient.OnNewConnection += (byte packetId, ConnectionEvent eventData) =>
                RegisterNewCar(eventData.CarId, eventData.CarModel, eventData.DriverName);
            _acsClient.OnCarInfo += (byte packetId, CarInfoEvent eventData) =>
                RegisterCar(eventData.CarId, eventData.Model, eventData.DriverName);
            _acsClient.OnConnectionClosed += (byte packetId, ConnectionEvent eventData) =>
                UnregisterCar(eventData.CarId);
        }

        public void RegisterNewCar(int carId, string carName, string driverName)
        {
            lock (_lock)
            {
                // 기존의 등록된 데이터는 무조건 쓰레기다.
                // 서버가 재시작 되어서 이렇게 될 가능성이 높다.
                UnregisterCar(carId);
                RegisterCar(carId, carName, driverName);
            }
        }

        public void RegisterCar(int carId, string carName, string driverName)
        {
            lock (_lock)
            {

                var car = _cars.TryGetValue(carId);
                if (car != null)
                {
                    // 이런 케이스는 발생하지 않지만 방어코드로 만들어 둔다.

                    var changed = false;
                    if (car.CarName != carName)
                    {
                        Console.WriteLine($"carId[{carId}]'s model is changed: {car.CarName} -> {carName}");
                        changed = true;
                    }
                    if (car.DriverName != driverName)
                    {
                        Console.WriteLine($"carId[{carId}]'s driver is changed: {car.DriverName} -> {driverName}");
                        changed = true;
                    }

                    if (changed)
                    {
                        UnregisterCar(carId);
                    }
                    else
                    {
                        // 바뀐 것이 없으므로 그냥 나간다
                        return;
                    }
                }

                car = new Car(carName, driverName);
                _cars.Add(carId, car);

                try
                {
                    var waiter = _getWaiters.Pop(carId);
                    waiter?.SetResult(car);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"waiter.SetResult() failed: {e.Message}");
                }
            }
        }
        
        public void UnregisterCar(int carId)
        {
            lock (_lock)
            {
                var car = _cars.TryGetValue(carId);
                if (car != null)
                {
                    Console.WriteLine($"Car({carId}) is unregistered.");
                    _cars.Remove(carId);
                }

                var waiter = _getWaiters.Pop(carId);
                if (waiter != null)
                {
                    Console.WriteLine($"Car waiter ({carId}) will be canceled.");
                    try
                    {
                        waiter.SetCanceled();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Waiter cannot be canceled: {e.Message}");
                    }
                }
            }
        }

        // 멀티 서버가 재시작되던가 하면 task가 cancel 될 수 있다.
        public async Task<Car> GetAsync(byte carId)
        {
            TaskCompletionSource<Car> waiter;
            lock (_lock)
            {
                var car = _cars.TryGetValue(carId);
                if (car != null)
                {
                    return car;
                }

                waiter = _getWaiters.TryGetValue(carId);
                if (waiter != null && waiter.Task.IsCompleted)
                {
                    _getWaiters.Remove(carId);
                    waiter = null;
                }
                if (waiter == null)
                {
                    waiter = new TaskCompletionSource<Car>();
                    _getWaiters.Add(carId, waiter);
                }
            }

            await _acsClient.GetCarInfoAsync(carId);
            // 이제 서버에서 응답이 오면 RegisterCar()가 호출되면서 waiter가 complete 될 것이다.

            var timeout = Task.Delay(5000);

            if (await Task.WhenAny(waiter.Task, timeout) == timeout)
            {
                throw new TaskCanceledException("timeout");
            }
            else
            {
                return waiter.Task.Result;
            }
        }
    }
}
