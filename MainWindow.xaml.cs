using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Newtonsoft.Json;
using QuikSharp;
using QuikSharp.DataStructures;
using QuikSharp.DataStructures.Transaction;
using Condition = QuikSharp.DataStructures.Condition;

namespace TrailingStop
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Quik _quik;
        private Tool _tool;

        private Random rnd = new Random();

        Dictionary<long, bool> orders = new Dictionary<long, bool>();
        Dictionary<long, bool> stopOrders = new Dictionary<long, bool>();

        bool isServerConnected = false;
        bool isRobotActive = false;
        bool isAutoEnable = false;

        string path = @"strategy.txt";

        string secCode = "SBER"; //  SBER RIH0 SiH0
        string classCode = "";
        string clientCode = "";
        object locker = new object();
        string comment = "a";
        string commentStop = "b";
        private int stop = 9;
        private int take = 9;
        private int delta = 3;
        private int slip = 10;  //  проскальзывание

        private long linkedOrderNum = 0;
        private long stopTransId = 0;

        private Strategy strategy = new Strategy();

        public MainWindow()
        {
            InitializeComponent();
            ButtonRun.IsEnabled = false;
            ButtonTrailingStop.IsEnabled = false;
            ButtonTest.IsEnabled = false;
            ButtonCancel.IsEnabled = false;
            ButtonAuto.IsEnabled = false;
            TextBoxSecCode.Text = secCode;
            TextBoxName.Text = comment;
            TextBoxName.MaxLength = 8;
            TextBoxStopLoss.Text = stop.ToString();
            TextBoxTakeProfit.Text = take.ToString();
            TextBoxDelta.Text = delta.ToString();
            LabelStrategyStatus.Content = "Неактивна";

            strategy.IsActive = false;
        }

        private void ButtonConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TextBoxLogsWindow.AppendText("Подключаемся к терминалу Quik..." + Environment.NewLine);
                _quik = new Quik(Quik.DefaultPort, new InMemoryStorage()); // инициализируем объект Quik с использованием локального расположения терминала (по умолчанию)
            }
            catch
            {
                TextBoxLogsWindow.AppendText("Ошибка инициализации объекта Quik..." + Environment.NewLine);
            }

            if (_quik != null)
            {
                TextBoxLogsWindow.AppendText("Экземпляр Quik создан." + Environment.NewLine);
                try
                {
                    TextBoxLogsWindow.AppendText("Получаем статус соединения с сервером...." + Environment.NewLine);
                    isServerConnected = _quik.Service.IsConnected().Result;
                    if (isServerConnected)
                    {
                        TextBoxLogsWindow.AppendText("Соединение с сервером установлено." + Environment.NewLine);
                        ButtonConnect.IsEnabled = false;
                        ButtonRun.IsEnabled = true;
                    }
                    else
                    {
                        TextBoxLogsWindow.AppendText("Соединение с сервером НЕ установлено." + Environment.NewLine);
                        ButtonConnect.IsEnabled = false;
                        ButtonRun.IsEnabled = true;
                    }
                }
                catch
                {
                    TextBoxLogsWindow.AppendText("Неудачная попытка получить статус соединения с сервером." + Environment.NewLine);
                }
            }
        }

        private void ButtonRun_Click(object sender, RoutedEventArgs e)
        {
            Run();
        }

        void Run()
        {
            comment = TextBoxName.Text;
            stop = Int32.Parse(TextBoxStopLoss.Text);
            take = Int32.Parse(TextBoxTakeProfit.Text);
            delta = Int32.Parse(TextBoxDelta.Text);
            try
            {
                secCode = TextBoxSecCode.Text;
                TextBoxLogsWindow.AppendText("Определяем код класса инструмента " + secCode + ", по списку классов..." + Environment.NewLine);
                try
                {
                    classCode = _quik.Class.GetSecurityClass("SPBFUT,TQBR,TQBS,TQNL,TQLV,TQNE,TQOB,QJSIM", secCode).Result;
                }
                catch
                {
                    TextBoxLogsWindow.AppendText("Ошибка определения класса инструмента. Убедитесь, что тикер указан правильно" + Environment.NewLine);
                }

                if (classCode != null && classCode != "")
                {
                    TextBoxLogsWindow.AppendText("Определяем код клиента..." + Environment.NewLine);
                    clientCode = _quik.Class.GetClientCode().Result;
                    TextBoxLogsWindow.AppendText("Создаем экземпляр инструмента " + secCode + "|" + classCode + "..." + Environment.NewLine);
                    _tool = new Tool(_quik, secCode, classCode);
                    if (_tool != null && _tool.Name != null && _tool.Name != "")
                    {
                        TextBoxLogsWindow.AppendText("Инструмент " + _tool.Name + " создан." + Environment.NewLine);
                    }

                    ButtonRun.IsEnabled = false;
                    ButtonTrailingStop.IsEnabled = true;
                    ButtonAuto.IsEnabled = true;
                    ButtonTest.IsEnabled = true;

                    TextBoxLogsWindow.AppendText("Подписываемся на колбэк 'OnOrder'..." + Environment.NewLine);
                    _quik.Events.OnOrder += Events_OnOrder;

                    TextBoxLogsWindow.AppendText("Подписываемся на колбэк 'OnStopOrder'..." + Environment.NewLine);
                    _quik.Events.OnStopOrder += Events_OnStopOrder;

                    TextBoxLogsWindow.AppendText("Подписываемся на колбэк 'OnParam'..." + Environment.NewLine);
                    _quik.Events.OnParam += Events_OnParam;

                    TextBoxLogsWindow.AppendText("Подписываемся на колбэк 'OnTransReply'..." + Environment.NewLine);
                    _quik.Events.OnTransReply += Events_OnTransReply;

                    /*
                    TextBoxLogsWindow.AppendText("Подписываемся на колбэк 'OnTrade'..." + Environment.NewLine);
                    _quik.Events.OnTrade += Events_OnTrade;
                    */
                    TextBoxSecCode.IsEnabled = false;
                    TextBoxName.IsEnabled = false;
                    TextBoxStopLoss.IsEnabled = false;
                    TextBoxTakeProfit.IsEnabled = false;
                    TextBoxDelta.IsEnabled = false;
                }
            }
            catch
            {
                TextBoxLogsWindow.AppendText("Ошибка получения данных по инструменту." + Environment.NewLine);
            }
        }

        private void Events_OnTransReply(TransactionReply transReply)
        {
            
            int status = transReply.Status;
            /*
            if (transReply.Status == 2)
                status = "ошибка при передаче транзакции в торговую систему";
            if (transReply.Status == 3)
                status = "транзакция выполнена";
            if (transReply.Status > 3)
                status = "ошибка исполнения транзакции " + transReply.Status;

            Log("OnTransReply " + transReply.TransID + " статус " + status + " " + transReply.ResultMsg);
            */
            if (transReply.TransID == stopTransId)
            {
                switch (transReply.Status)
                {
                    case 1:
                        break;
                    case 2:
                        Log("Ошибка при передаче транзакции в торговую систему " + status + " " + transReply.ResultMsg);
                        break;
                    case 3:
                        Log("Стоп-заявка зарегистрирована, номер " + transReply.OrderNum);
                        stopTransId = 0;
                        break;
                    default:
                        Log("Ошибка исполнения транзакции " + status + " " + transReply.ResultMsg);
                        break;
                }
            }

        }

        private void ButtonTrailingStop_Click(object sender, RoutedEventArgs e)
        {
            if (!isRobotActive)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        using (StreamReader sr = new StreamReader(path, System.Text.Encoding.Default))
                        {
                            var line = sr.ReadLine();
                            strategy = JsonConvert.DeserializeObject<Strategy>(line);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Log("Ошибка чтения стратегии из файла " + exception);
                }

                if (strategy.IsActive)
                {
                    Log("Есть сохраненная стратегия, стоп-заявка " + strategy.StopOrderNum);
                    var stopOrders = _quik.StopOrders.GetStopOrders(classCode, secCode).Result;
                    var myStop = from stop in stopOrders
                                 where stop.OrderNum == strategy.StopOrderNum && stop.State == State.Active
                                 select stop;
                    if (myStop.Count() > 0)
                    {
                        Log("Есть активная стоп-заявка " + strategy.StopOrderNum + " подключаем слежение");
                        UpdateLabelStrategy();
                    }
                    else
                    {
                        Log("Активной стоп-заявки нет. Обнуляем стратегию.");
                        strategy.IsActive = false;
                        UpdateLabelStrategy();
                        Save2File();
                    }
                }

                isRobotActive = true;
                ButtonTrailingStop.Content = "Stop";

                Log("Запускаем робота ...");
            }
            else //  && ButtonTrailingStop.Content.ToString() == "TrailingStop"
            {
                Log("Робот активен! Останавливаем?");
                if (true)
                {
                    Log("Всё остановили!!! :)");
                    isRobotActive = false;
                    ButtonTrailingStop.Content = "TrailingStop";
                    strategy.IsActive = false;
                    UpdateLabelStrategy();
                }
            }
        }

        private void Events_OnStopOrder(StopOrder stopOrder)
        {
            // Log("Изменилась стоп-заявка " + stopOrder.OrderNum + " " + stopOrder.State + " " + stopOrder.Comment + " " + stopOrder.LinkedOrder + " флаги " + stopOrder.Flags);
            if (stopOrder.Comment.Contains(commentStop) && stopOrder.SecCode == secCode && stopOrder.State == State.Completed)
            {
                lock (stopOrders)
                {
                    try
                    {
                        if (!stopOrders.ContainsKey(stopOrder.OrderNum))
                        {
                            stopOrders.Add(stopOrder.OrderNum, true);

                            strategy.IsActive = false;
                            UpdateLabelStrategy();
                            linkedOrderNum = stopOrder.LinkedOrder;

                            Log("Сработало условие стоп-заявки " + stopOrder.OrderNum + " и выставилась заявка " + linkedOrderNum);
                            Save2File();
                        }
                    }
                    catch (Exception e)
                    {
                        Log("OnStopOrder " + stopOrder.OrderNum + " " + e.ToString());
                    }
                }
            }
            // 32797 бит 15 (0x8000)  Идет расчет минимума-максимума

            if ((stopOrder.Flags & 0x8000) != 0)
            {
                strategy.IsActive = false;
                UpdateLabelStrategy();
                Log("Идет расчет минимума-максимума");
            }
        }

        private void Events_OnParam(Param par)
        {
            // Log("Событие Events_OnParam " + par.SecCode);
            if (isRobotActive && strategy.IsActive && par.SecCode == secCode)
            {
                lock (locker)
                {
                    var last = _quik.Trading.GetParamEx(classCode, secCode, ParamNames.LAST).Result.ParamImage;
                    //Log("Событие Events_OnParam " + par.SecCode + " Цена " + last);
                    if (strategy.Operation == Operation.Buy)
                    {
                        //Log("Вход " + strategy.PricePosition + " - цена " + last + " = " + (strategy.PricePosition - Decimal.Parse(last)));
                        if (strategy.PricePosition - Decimal.Parse(last) >= delta * _tool.Step)
                        {
                            decimal oldPricePosition = Math.Round(strategy.PricePosition, _tool.PriceAccuracy);
                            Log("Цена " + last + " ушла вниз от точки входа в шорт " + oldPricePosition + " на " + Math.Round(delta * _tool.Step, _tool.PriceAccuracy));
                            strategy.PricePosition -= Math.Round(delta * _tool.Step, _tool.PriceAccuracy);
                            Log("Меняем PricePosition " + oldPricePosition + " -> " + strategy.PricePosition);
                            Log("Снимаем стоп-заявку на покупку " + strategy.StopOrderNum + " и выставляем новую");
                            StopOrder stopOrder = new StopOrder()
                            {
                                ClassCode = classCode,
                                OrderNum = strategy.StopOrderNum,
                                SecCode = secCode
                            };
                            long x = _quik.StopOrders.KillStopOrder(stopOrder).Result;
                            Log("Выставлена транзакция " + x + " на снятие стоп-заявки " + stopOrder.OrderNum);
                            // перенести в OnTransReply
                            CreateStopOrder(strategy.Condition, strategy.Operation, strategy.CondPrice, strategy.CondPrice2 - delta * _tool.Step, strategy.CondPrice2 - (delta - slip) * _tool.Step, strategy.Qty, strategy.PricePosition);
                        }
                    }
                    if (strategy.Operation == Operation.Sell)
                    {
                        //Log("Цена " + last + " - вход " + strategy.PricePosition + " = " + (Decimal.Parse(last) - strategy.PricePosition));
                        if (Decimal.Parse(last) - strategy.PricePosition >= delta * _tool.Step)
                        {
                            decimal oldPricePosition = Math.Round(strategy.PricePosition, _tool.PriceAccuracy);
                            Log("Цена " + last + " ушла вверх от точки входа в лонг " + oldPricePosition + " на " + Math.Round(delta * _tool.Step, _tool.PriceAccuracy));
                            strategy.PricePosition += Math.Round(delta * _tool.Step, _tool.PriceAccuracy);
                            Log("Меняем PricePosition " + oldPricePosition + " -> " + strategy.PricePosition);
                            Log("Снимаем стоп-заявку на продажу " + strategy.StopOrderNum + " и выставляем новую");
                            StopOrder stopOrder = new StopOrder()
                            {
                                ClassCode = classCode,
                                OrderNum = strategy.StopOrderNum,
                                SecCode = secCode
                            };
                            long x = _quik.StopOrders.KillStopOrder(stopOrder).Result;
                            Log("Выставлена транзакция " + x + " на снятие стоп-заявки " + stopOrder.OrderNum);
                            // перенести в OnTransReply
                            CreateStopOrder(strategy.Condition, strategy.Operation, strategy.CondPrice, strategy.CondPrice2 + delta * _tool.Step, strategy.CondPrice2 + (delta - slip) * _tool.Step, strategy.Qty, strategy.PricePosition);
                        }
                    }
                }
            }
        }

        private void Events_OnOrder(Order order)
        {
            // обрабатываем только наши исполненные заявки
            //Log("OrderNum " + order.OrderNum + " TransID " + order.TransID + " состояние " + order.State + " флаги " + order.Flags);
            /*
            if (order.TransID > 0)
            {
                Log("order.TransID > 0");
            }
            */
            if (order.Comment.Contains(commentStop) && order.State == State.Completed && order.TransID > 0) // && order.OrderNum == linkedOrderNum
            {
                lock (orders)
                {
                    if (!orders.ContainsKey(order.OrderNum))
                    {
                        orders.Add(order.OrderNum, true);
                        // заявки по стоп-заявкам
                        Log("OrderNum " + order.OrderNum + " TransID " + order.TransID + " состояние " + order.State + " " + order.Comment);
                        if (isAutoEnable)
                        {
                            OpenNewPosition();
                        }
                    }
                }
            }

            // у заявок из терминала TransID = 0
            // пришла наша исполненная заявка по неактивной стратегии
            if (!strategy.IsActive && order.Comment.Contains(comment) && order.SecCode == secCode && order.State == State.Completed)
            {
                lock (orders)
                {
                    if (!orders.ContainsKey(order.OrderNum))
                    {
                        orders.Add(order.OrderNum, true);
                        Log("-----------------------------------------");
                        Log("Наша заявка " + order.Operation + " OrderNum " + order.OrderNum + " TransID " + order.TransID + " состояние " + order.State);

                        var listTrades = _quik.Trading.GetTrades().Result;
                        var myTrades = from trade in listTrades
                                       where trade.SecCode == secCode && trade.Comment.Contains(comment) && trade.OrderNum == order.OrderNum
                                       select trade;
                        int qty = 0;
                        decimal sum = 0;
                        foreach (Trade trade in myTrades)
                        {
                            Log("Наша сделка TradeNum " + trade.TradeNum + " OrderNum " + trade.OrderNum + " SecCode " + trade.SecCode + " Цена " + trade.Price + " Кол-во " + trade.Quantity);
                            qty += trade.Quantity;
                            sum += trade.Quantity * (decimal)trade.Price;
                        }
                        /* для long
                        Condition = Condition.LessOrEqual,
                        Price = priceIn - 25 * _tool.Step, // цена заявки по стопу
                        ConditionPrice = Math.Round(priceIn + 20 * _tool.Step, _tool.PriceAccuracy), // тейк-профит
                        ConditionPrice2 = Math.Round(priceIn - 20 * _tool.Step, _tool.PriceAccuracy), // стоп-лимит
                        Operation = Operation.Sell,
                        */

                        var pricePosition = sum / qty;
                        var condPrice = order.Operation == Operation.Sell ? (sum / qty - take * _tool.Step) : (sum / qty + take * _tool.Step);  // тейк-профит
                        var condPrice2 = order.Operation == Operation.Sell ? (sum / qty + stop * _tool.Step) : (sum / qty - stop * _tool.Step); // стоп-лимит
                        var condition = order.Operation == Operation.Sell ? Condition.MoreOrEqual : Condition.LessOrEqual;
                        var operation = order.Operation == Operation.Sell ? Operation.Buy : Operation.Sell;
                        var price = order.Operation == Operation.Sell ? condPrice2 + slip * _tool.Step : condPrice2 - slip * _tool.Step; // цена заявки по стопу

                        CreateStopOrder(condition, operation, condPrice, condPrice2, price, qty, pricePosition);
                    }
                }
            }
        }

        private void OpenNewPosition()
        {
            Random rnd = new Random();
            int i = rnd.Next(1, 2000) % 2;
            Thread.Sleep(25000);
            try
            {
                Order order = new Order()
                {
                    Account = _tool.AccountID,
                    ClassCode = classCode,
                    SecCode = secCode,
                    Quantity = 1,
                    Operation = i==0 ? Operation.Buy: Operation.Sell,
                    Price = i == 0 ? Math.Round(_tool.LastPrice + 30 * _tool.Step, _tool.PriceAccuracy) : Math.Round(_tool.LastPrice - 30 * _tool.Step, _tool.PriceAccuracy),
                    ClientCode = comment
                };
                var res = _quik.Orders.CreateOrder(order).Result;
                Log("OpenNewPosition выставляем новую заявку " + res);
            }
            catch (Exception exception)
            {
                Log("OpenNewPosition ошибка выставления заявки " + exception.ToString());
            }
        }

        private void EnterLong()
        {
            try
            {
                Order order = new Order()
                {
                    Account = _tool.AccountID,
                    ClassCode = classCode,
                    SecCode = secCode,
                    Quantity = 1,
                    Operation = Operation.Buy,
                    Price = Math.Round(_tool.LastPrice + 20 * _tool.Step, _tool.PriceAccuracy),
                    ClientCode = comment
                };
                var res = _quik.Orders.CreateOrder(order).Result;
                Log("Выставляем новую заявку " + res);
            }
            catch (Exception exception)
            {
                Log("Ошибка выставления заявки " + exception.ToString());
            }
        }
        private void Events_OnTrade(Trade trade)
        {
            // не нужно!!!
            if (trade.Comment.Contains(comment) && trade.SecCode == secCode)   // order.TransID > 0 
            {
                // Log("Наша сделка TradeNum " + trade.TradeNum + " OrderNum " + trade.OrderNum + " SecCode " + trade.SecCode + " Quantity " + trade.Quantity + " комментарий " + trade.Comment);
            }
        }

        private async void CreateStopOrder(Condition condition, Operation operation, decimal condPrice, decimal condPrice2, decimal price, int qty, decimal pricePosition)
        {
            StopOrder orderNew = new StopOrder()
            {
                Account = _tool.AccountID,
                ClassCode = _tool.ClassCode,
                ClientCode = commentStop, // clientCode,
                SecCode = secCode,
                StopOrderType = StopOrderType.TakeProfitStopLimit,
                Condition = condition,
                Operation = operation,
                ConditionPrice = (decimal)Math.Round(condPrice, _tool.PriceAccuracy),
                Price = (decimal)Math.Round(price, _tool.PriceAccuracy),
                ConditionPrice2 = (decimal)Math.Round(condPrice2, _tool.PriceAccuracy),
                Offset = 0,
                OffsetUnit = OffsetUnits.PRICE_UNITS,
                Spread = 0,
                SpreadUnit = OffsetUnits.PRICE_UNITS,
                MarketStopLimit = YesOrNo.YES,
                MarketTakeProfit = YesOrNo.YES,
                Quantity = qty
                //Comment = comment
            };

            try
            {
                //var res = _quik.StopOrders.CreateStopOrder(orderNew);
                long transID = await _quik.StopOrders.CreateStopOrder(orderNew).ConfigureAwait(false);
                Log("Выставили тейк-профит стоп-лимит на " + orderNew.Operation + " по цене стоп " + orderNew.ConditionPrice2 + " тейк " + orderNew.ConditionPrice + " TransID " + transID);
                if (transID > 0)
                {
                    Log("Стоп-заявка выставлена. ID транзакции - " + transID);
                    stopTransId = transID;
                    
                    Thread.Sleep(1000);
                    try
                    {
                        var listStopOrders = _quik.StopOrders.GetStopOrders().Result;
                        foreach (StopOrder stopOrder in listStopOrders)
                        {
                            if (stopOrder.TransId == transID && stopOrder.ClassCode == _tool.ClassCode && stopOrder.SecCode == _tool.SecurityCode)
                            {
                                strategy.StopOrderNum = stopOrder.OrderNum;
                                Log("Стоп-заявка номер " + stopOrder.OrderNum + " выставлена");
                            }
                        }
                        strategy.Name = comment;
                        strategy.IsActive = true;
                        strategy.PricePosition = pricePosition;
                        strategy.Operation = orderNew.Operation;
                        strategy.Condition = condition;
                        strategy.CondPrice = orderNew.ConditionPrice;
                        strategy.CondPrice2 = orderNew.ConditionPrice2;
                        strategy.Price = orderNew.Price;
                        strategy.Qty = qty;

                        UpdateLabelStrategy();
                        Save2File();
                    }
                    catch { Log("Ошибка получения номера стоп-заявки."); }
                
                }
                else Log("Неудачная попытка выставления стоп-заявки.");

            }
            catch (Exception exception)
            {
                Log("Ошибка выставления заявки CreateStopOrder " + exception);
            }
        }

        private void Log(string str)    // потоки
        {
            try
            {
                this.Dispatcher.Invoke(() =>
                {
                    TextBoxLogsWindow.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + " " + str + Environment.NewLine);
                    TextBoxLogsWindow.ScrollToLine(TextBoxLogsWindow.LineCount - 1);  // прокрутка scroll
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private void Save2File()
        {
            try
            {
                string json = JsonConvert.SerializeObject(strategy);
                var file = new StreamWriter(path);
                file.Write(json);
                file.Close();
            }
            catch (Exception e)
            {
                Log("Ошибка сохранения стратегии в файл " + e);
            }
        }

        private void ButtonTest_Click(object sender, RoutedEventArgs e)
        {
            var qty = 1;
            var pricePosition = 262;
            var operation = Operation.Sell;
            var condPrice = 263;  // тейк-профит
            var condPrice2 = 261; // стоп-лимит
            var condition = Condition.LessOrEqual;
            var price = 260; // цена заявки по стопу

            CreateStopOrder(condition, operation, condPrice, condPrice2, price, qty, pricePosition);
        }

        private void ButtonCancel_Click(object sender, RoutedEventArgs e)
        {
            var cancel = Int32.Parse(TextBoxCancel.Text);
            StopOrder stopOrder = new StopOrder()
            {
                ClassCode = classCode,
                OrderNum = cancel,
                SecCode = secCode
            };
            long x = _quik.StopOrders.KillStopOrder(stopOrder).Result;
            Log("Выставлена транзакция " + x);
            stopTransId = x;
        }

        private void UpdateLabelStrategy()
        {
            string content = strategy.IsActive ? "Активна" : "Неактивна";

            Action action = () => LabelStrategyStatus.Content = content;
            Dispatcher.Invoke(action);
        }

        private void ButtonAuto_Click(object sender, RoutedEventArgs e)
        {
            if (ButtonAuto.Content.ToString() == "Auto")
            {
                isAutoEnable = true;
                ButtonAuto.Content = "StopAuto";
                Log("Режим автооткрытия позции в случайную сторону включен");
            }
            else
            {
                isAutoEnable = false;
                ButtonAuto.Content = "Auto";
                Log("Режим автооткрытия позции выключен");
            }
        }
    }
}
