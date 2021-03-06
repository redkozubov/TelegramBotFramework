﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using TelegramBotBase.Args;
using TelegramBotBase.Attributes;
using TelegramBotBase.Base;
using TelegramBotBase.Form;
using TelegramBotBase.Interfaces;
using TelegramBotBase.Sessions;

namespace TelegramBotBase
{
    /// <summary>
    /// Bot base class for full Device/Context and Messagehandling
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BotBase<T>
        where T : FormBase
    {
        public MessageClient Client { get; set; }

        /// <summary>
        /// Your TelegramBot APIKey
        /// </summary>
        public String APIKey { get; set; } = "";

        /// <summary>
        /// List of all running/active sessions
        /// </summary>
        public SessionBase Sessions { get; set; }

        /// <summary>
        /// Contains System commands which will be available at everytime and didnt get passed to forms, i.e. /start
        /// </summary>
        public List<String> SystemCalls { get; set; }

        private EventHandlerList __Events = new EventHandlerList();

        private static object __evSessionBegins = new object();

        private static object __evMessage = new object();

        private static object __evSystemCall = new object();

        public delegate Task SystemCallEventHandler(object sender, SystemCallEventArgs e);

        private static object __evException = new object();

        private static object __evUnhandledCall = new object();


        /// <summary>
        /// Skips all messages during running (good for big delay updates)
        /// </summary>
        public bool SkipAllMessages { get; set; } = false;

        /// <summary>
        /// Loggs all messages and sent them to the event handler
        /// </summary>
        public bool LogAllMessages { get; set; } = false;

        /// <summary>
        /// Enable the SessionState (you need to implement on call forms the IStateForm interface)
        /// </summary>
        public IStateMachine StateMachine { get; set; }

        /// <summary>
        /// How often could a form navigate to another (within one user action/call/message)
        /// </summary>
        private const int NavigationMaximum = 10;

        /// <summary>
        /// Simple start of your Bot with the APIKey
        /// </summary>
        /// <param name="apiKey"></param>
        public BotBase(String apiKey)
        {
            this.APIKey = apiKey;

            this.Client = new Base.MessageClient(this.APIKey);
            this.Client.TelegramClient.Timeout = new TimeSpan(0, 1, 0);

            this.SystemCalls = new List<string>();

            this.Sessions = new SessionBase();
            this.Sessions.Client = this.Client;
        }

        /// <summary>
        /// Simple start of your Bot with the APIKey and a proxyAdress
        /// </summary>
        /// <param name="apiKey"></param>
        /// <param name="proxyBaseAddress">i.e. https://127.0.0.1:10000</param>
        public BotBase(String apiKey, System.Net.Http.HttpClient proxy)
        {
            this.APIKey = apiKey;

            this.Client = new Base.MessageClient(this.APIKey, proxy);

            this.SystemCalls = new List<string>();

            this.Sessions = new SessionBase();
            this.Sessions.Client = this.Client;
        }

        /// <summary>
        /// Simple start of your Bot with the APIKey and a TelegramBotClient instance.
        /// </summary>
        /// <param name="apiKey"></param>
        /// <param name="client"></param>
        public BotBase(String apiKey, TelegramBotClient client)
        {
            this.APIKey = apiKey;

            this.Client = new Base.MessageClient(this.APIKey, client);

            this.SystemCalls = new List<string>();

            this.Sessions = new SessionBase();
            this.Sessions.Client = this.Client;
        }

        /// <summary>
        /// Simple start of your Bot with the APIKey and a proxyAdress
        /// </summary>
        /// <param name="apiKey"></param>
        /// <param name="proxyBaseAddress">i.e. https://127.0.0.1:10000</param>
        public BotBase(String apiKey, String proxyBaseAddress)
        {
            this.APIKey = apiKey;

            var url = new Uri(proxyBaseAddress);

            this.Client = new Base.MessageClient(this.APIKey, url);

            this.SystemCalls = new List<string>();

            this.Sessions = new SessionBase();
            this.Sessions.Client = this.Client;
        }

        /// <summary>
        /// Simple start of your Bot with the APIKey and a proxyAdress
        /// </summary>
        /// <param name="apiKey"></param>
        /// <param name="proxyHost">i.e. 127.0.0.1</param>
        /// <param name="proxyPort">i.e. 10000</param>
        public BotBase(String apiKey, String proxyHost, int proxyPort)
        {
            this.APIKey = apiKey;

            this.Client = new Base.MessageClient(this.APIKey, proxyHost, proxyPort);

            this.SystemCalls = new List<string>();

            this.Sessions = new SessionBase();
            this.Sessions.Client = this.Client;
        }

        /// <summary>
        /// Start your Bot
        /// </summary>
        public void Start()
        {
            if (this.Client == null)
                return;

            this.Client.Message += Client_Message;
            this.Client.MessageEdit += Client_MessageEdit;
            this.Client.Action += Client_Action;

            if (this.StateMachine != null)
            {
                LoadSessionStates();
            }

            this.Client.TelegramClient.StartReceiving();
        }


        /// <summary>
        /// Stop your Bot
        /// </summary>
        public void Stop()
        {
            if (this.Client == null)
                return;

            this.Client.Message -= Client_Message;
            this.Client.Action -= Client_Action;

            this.Client.TelegramClient.StopReceiving();

            if (this.StateMachine != null)
            {
                SaveSessionStates();
            }
        }

        /// <summary>
        /// Send a message to all active Sessions.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public async Task SentToAll(String message)
        {
            if (this.Client == null)
                return;

            foreach (var s in this.Sessions.SessionList)
            {
                await this.Client.TelegramClient.SendTextMessageAsync(s.Key, message);
            }
        }

        private async void Client_Message(object sender, MessageResult e)
        {
            if (this.SkipAllMessages)
                return;

            try
            {
                DeviceSession ds = this.Sessions.GetSession(e.DeviceId);
                e.Device = ds;

                if (LogAllMessages)
                {
                    OnMessage(new MessageIncomeEventArgs(e.DeviceId, ds, e));
                }

                ds?.OnMessageReceived(new MessageReceivedEventArgs(e.Message));

                await Client_TryMessage(sender, e);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            {

            }
            catch (Exception ex)
            {
                DeviceSession ds = this.Sessions.GetSession(e.DeviceId);
                OnException(new SystemExceptionEventArgs(e.Message.Text, ds?.DeviceId ?? -1, ds, ex));
            }
        }

        private async Task Client_TryMessage(object sender, MessageResult e)
        {
            DeviceSession ds = e.Device;
            if (ds == null)
            {
                ds = await this.Sessions.StartSession<T>(e.DeviceId);
                e.Device = ds;

                ds.LastMessage = e.Message;

                OnSessionBegins(new SessionBeginEventArgs(e.DeviceId, ds));
            }

            ds.ChatTitle = e.Message.Chat.Username ?? e.Message.Chat.Title;
            ds.LastAction = DateTime.Now;
            ds.LastMessage = e.Message;

            //Is this a systemcall ?
            if (e.IsSystemCall && this.SystemCalls.Contains(e.SystemCommand))
            {
                var sce = new SystemCallEventArgs(e.SystemCommand, e.SystemCallParameters, e.Message, ds.DeviceId, ds);
                await OnSystemCall(sce);

                if (sce.Handled)
                    return;
            }

            FormBase activeForm = null;

            int i = 0;

            //Should formulars get navigated (allow maximum of 10, to dont get loops)
            do
            {
                i++;

                //Reset navigation
                ds.FormSwitched = false;

                activeForm = ds.ActiveForm;

                //Pre Loading Event
                await activeForm.PreLoad(e);

                //Send Load event to controls
                await activeForm.LoadControls(e);

                //Loading Event
                await activeForm.Load(e);

                //Is Attachment ? (Photo, Audio, Video, Contact, Location, Document)
                if (e.Message.Type == Telegram.Bot.Types.Enums.MessageType.Contact | e.Message.Type == Telegram.Bot.Types.Enums.MessageType.Document | e.Message.Type == Telegram.Bot.Types.Enums.MessageType.Location |
                    e.Message.Type == Telegram.Bot.Types.Enums.MessageType.Photo | e.Message.Type == Telegram.Bot.Types.Enums.MessageType.Video | e.Message.Type == Telegram.Bot.Types.Enums.MessageType.Audio)
                {
                    await activeForm.SentData(new DataResult(e));
                }

                //Render Event
                if (!ds.FormSwitched)
                {
                    await activeForm.RenderControls(e);

                    await activeForm.Render(e);
                }

                e.IsFirstHandler = false;

            } while (ds.FormSwitched && i < NavigationMaximum);


        }

        private async void Client_MessageEdit(object sender, MessageResult e)
        {
            if (this.SkipAllMessages)
                return;

            try
            {
                DeviceSession ds = this.Sessions.GetSession(e.DeviceId);
                e.Device = ds;

                if (LogAllMessages)
                {
                    OnMessage(new MessageIncomeEventArgs(e.DeviceId, ds, e));
                }

                //Call same, to handle received liked edited
                ds?.OnMessageReceived(new MessageReceivedEventArgs(e.Message));

                await Client_TryMessageEdit(sender, e);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex)
            {

            }
            catch (Exception ex)
            {
                DeviceSession ds = this.Sessions.GetSession(e.DeviceId);
                OnException(new SystemExceptionEventArgs(e.Message.Text, ds?.DeviceId ?? -1, ds, ex));
            }
        }

        private async Task Client_TryMessageEdit(object sender, MessageResult e)
        {
            DeviceSession ds = e.Device;
            if (ds == null)
            {
                ds = await this.Sessions.StartSession<T>(e.DeviceId);
                e.Device = ds;
            }

            ds.ChatTitle = e.Message.Chat.Username ?? e.Message.Chat.Title;
            ds.LastAction = DateTime.Now;
            ds.LastMessage = e.Message;

            //Pre Loading Event
            await ds.ActiveForm.Edited(e);

            //When form has been switched due navigation within the edit method, reopen Client_Message
            if (ds.FormSwitched)
            {
                await Client_TryMessage(sender, e);
            }

        }

        private void Client_Action(object sender, MessageResult e)
        {
            try
            {
                DeviceSession ds = this.Sessions.GetSession(e.DeviceId);
                e.Device = ds;

                if (LogAllMessages)
                {
                    OnMessage(new MessageIncomeEventArgs(e.DeviceId, ds, e));
                }

                Client_TryAction(sender, e);
            }
            catch (Exception ex)
            {
                DeviceSession ds = this.Sessions.GetSession(e.DeviceId);
                OnException(new SystemExceptionEventArgs(e.Message.Text, ds?.DeviceId ?? -1, ds, ex));
            }
        }

        private async void Client_TryAction(object sender, MessageResult e)
        {
            DeviceSession ds = e.Device;
            if (ds == null)
            {
                ds = await this.Sessions.StartSession<T>(e.DeviceId);
                e.Device = ds;
            }

            ds.ChatTitle = e.Message.Chat.Username ?? e.Message.Chat.Title;
            ds.LastAction = DateTime.Now;
            ds.LastMessage = e.Message;

            FormBase activeForm = null;

            int i = 0;

            //Should formulars get navigated (allow maximum of 10, to dont get loops)
            do
            {
                i++;

                //Reset navigation
                ds.FormSwitched = false;

                activeForm = ds.ActiveForm;

                //Pre Loading Event
                await activeForm.PreLoad(e);

                //Send Load event to controls
                await activeForm.LoadControls(e);

                //Loading Event
                await activeForm.Load(e);

                //Action Event
                if (!ds.FormSwitched)
                {
                    //Send Action event to controls
                    await activeForm.ActionControls(e);

                    //Send Action event to form itself
                    await activeForm.Action(e);

                    if (!e.Handled)
                    {
                        var uhc = new UnhandledCallEventArgs(e.Message.Text, e.RawData, ds.DeviceId, e.MessageId, e.Message, ds);
                        OnUnhandledCall(uhc);

                        if (uhc.Handled)
                        {
                            if (ds.FormSwitched)
                            {
                                continue;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }

                }

                //Render Event
                if (!ds.FormSwitched)
                {
                    await activeForm.RenderControls(e);

                    await activeForm.Render(e);
                }

                e.IsFirstHandler = false;

            } while (ds.FormSwitched && i < NavigationMaximum);

        }

        /// <summary>
        /// Loads the previously saved states from the machine.
        /// </summary>
        private async void LoadSessionStates()
        {
            if (this.StateMachine == null)
            {
                throw new ArgumentNullException("StateMachine", "No StateMachine defined. Please set one to property BotBase.StateMachine");
            }

            var container = this.StateMachine.LoadFormStates();

            foreach (var s in container.States)
            {
                Type t = Type.GetType(s.QualifiedName);
                if (t == null || !t.IsSubclassOf(typeof(FormBase)))
                {
                    continue;
                }

                var form = t.GetConstructor(new Type[] { }).Invoke(new object[] { }) as FormBase;

                if (s.Values != null && s.Values.Count > 0)
                {
                    var properties = s.Values.Where(a => a.Key.StartsWith("$"));
                    var fields = form.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Where(a => a.GetCustomAttributes(typeof(SaveState), true).Length != 0).ToList();

                    foreach (var p in properties)
                    {
                        var f = fields.FirstOrDefault(a => a.Name == p.Key.Substring(1));
                        if (f == null)
                            continue;

                        try
                        {
                            f.SetValue(form, p.Value);
                        }
                        catch (ArgumentException ex)
                        {

                        }
                        catch
                        {

                        }
                    }
                }

                //Is Subclass of IStateForm
                var iform = form as IStateForm;
                if (iform != null)
                {
                    var ls = new LoadStateEventArgs();
                    ls.Values = s.Values;
                    iform.LoadState(ls);
                }


                form.Client = Client;
                var device = new DeviceSession(s.DeviceId, form);

                device.ChatTitle = s.ChatTitle;

                this.Sessions.SessionList.Add(s.DeviceId, device);

                try
                {
                    await form.OnInit(new InitEventArgs());

                    await form.OnOpened(new EventArgs());
                }
                catch
                {
                    //Skip on exception
                    this.Sessions.SessionList.Remove(s.DeviceId);
                }
            }

        }

        /// <summary>
        /// Saves all open states into the machine.
        /// </summary>
        private void SaveSessionStates()
        {
            if (this.StateMachine == null)
            {
                throw new ArgumentNullException("StateMachine", "No StateMachine defined. Please set one to property BotBase.StateMachine");
            }

            var states = new List<StateEntry>();

            foreach (var s in Sessions.SessionList)
            {
                if (s.Value == null)
                {
                    continue;
                }

                var form = s.Value.ActiveForm;


                try
                {
                    var se = new StateEntry();
                    se.DeviceId = s.Key;
                    se.ChatTitle = s.Value.ChatTitle;
                    se.FormUri = form.GetType().FullName;
                    se.QualifiedName = form.GetType().AssemblyQualifiedName;

                    if (form.GetType().GetCustomAttributes(typeof(IgnoreState), true).Length != 0)
                    {
                        continue;
                    }

                    //Is Subclass of IStateForm
                    var iform = form as IStateForm;
                    if (iform != null)
                    {
                        //Loading Session states
                        SaveStateEventArgs ssea = new SaveStateEventArgs();
                        iform.SaveState(ssea);

                        se.Values = ssea.Values;
                    }

                    //Search for public properties with SaveState attribute
                    var fields = form.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Where(a => a.GetCustomAttributes(typeof(SaveState), true).Length != 0).ToList();

                    foreach (var f in fields)
                    {
                        var val = f.GetValue(form);

                        se.Values.Add("$" + f.Name, val);
                    }

                    states.Add(se);
                }
                catch
                {
                    //Continue on error (skip this form)
                    continue;
                }
            }

            var sc = new StateContainer();
            sc.States = states;

            this.StateMachine.SaveFormStates(new SaveStatesEventArgs(sc));
        }


        /// <summary>
        /// Will be called if a session/context gets started
        /// </summary>

        public event EventHandler<SessionBeginEventArgs> SessionBegins
        {
            add
            {
                this.__Events.AddHandler(__evSessionBegins, value);
            }
            remove
            {
                this.__Events.RemoveHandler(__evSessionBegins, value);
            }
        }

        public void OnSessionBegins(SessionBeginEventArgs e)
        {
            (this.__Events[__evSessionBegins] as EventHandler<SessionBeginEventArgs>)?.Invoke(this, e);

        }

        /// <summary>
        /// Will be called on incomming message
        /// </summary>
        public event EventHandler<MessageIncomeEventArgs> Message
        {
            add
            {
                this.__Events.AddHandler(__evMessage, value);
            }
            remove
            {
                this.__Events.RemoveHandler(__evMessage, value);
            }
        }

        public void OnMessage(MessageIncomeEventArgs e)
        {
            (this.__Events[__evMessage] as EventHandler<MessageIncomeEventArgs>)?.Invoke(this, e);

        }

        /// <summary>
        /// Will be called if a system call gets raised
        /// </summary>
        public event SystemCallEventHandler SystemCall;


        public async Task OnSystemCall(SystemCallEventArgs e)
        {
            if (this.SystemCall != null)
                await SystemCall(this, e);
        }

        /// <summary>
        /// Will be called on an inner exception
        /// </summary>
        public event EventHandler<SystemExceptionEventArgs> Exception
        {
            add
            {
                this.__Events.AddHandler(__evException, value);
            }
            remove
            {
                this.__Events.RemoveHandler(__evException, value);
            }
        }

        public void OnException(SystemExceptionEventArgs e)
        {
            (this.__Events[__evException] as EventHandler<SystemExceptionEventArgs>)?.Invoke(this, e);

        }

        /// <summary>
        /// Will be called if no form handeled this call
        /// </summary>
        public event EventHandler<UnhandledCallEventArgs> UnhandledCall
        {
            add
            {
                this.__Events.AddHandler(__evUnhandledCall, value);
            }
            remove
            {
                this.__Events.RemoveHandler(__evUnhandledCall, value);
            }
        }

        public void OnUnhandledCall(UnhandledCallEventArgs e)
        {
            (this.__Events[__evUnhandledCall] as EventHandler<UnhandledCallEventArgs>)?.Invoke(this, e);

        }
    }
}
