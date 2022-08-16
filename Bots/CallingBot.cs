// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using CallingBotSample.Interfaces;
using CallingBotSample.Utility;
using CallingMeetingBot.Extenstions;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Graph.Communications.Client.Authentication;
using Microsoft.Graph.Communications.Common.Telemetry;
using Microsoft.Graph.Communications.Core.Notifications;
using Microsoft.Graph.Communications.Core.Serialization;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text;




namespace CallingBotSample.Bots
{
    public class CallingBot : ActivityHandler
    {
        private readonly IConfiguration configuration;
        public IGraphLogger GraphLogger { get; }

        private IRequestAuthenticationProvider AuthenticationProvider { get; }

        private INotificationProcessor NotificationProcessor { get; }
        private CommsSerializer Serializer { get; }
        private readonly BotOptions options;

        private readonly ICard card;
        private readonly IGraph graph;
        private readonly IGraphServiceClient graphServiceClient;

        public CallingBot(BotOptions options, IConfiguration configuration, ICard card, IGraph graph, IGraphServiceClient graphServiceClient, IGraphLogger graphLogger)
        {
            this.options = options;
            this.configuration = configuration;
            this.card = card;
            this.graph = graph;
            this.graphServiceClient = graphServiceClient;
            this.GraphLogger = graphLogger;

            var name = this.GetType().Assembly.GetName().Name;
            this.AuthenticationProvider = new AuthenticationProvider(name, options.AppId, options.AppSecret, graphLogger);

            this.Serializer = new CommsSerializer();
            this.NotificationProcessor = new NotificationProcessor(Serializer);
            this.NotificationProcessor.OnNotificationReceived += this.NotificationProcessor_OnNotificationReceived;

        }

        /// <summary>
        /// Process "/callback" notifications asyncronously. 
        /// </summary>
        /// <param name="request"></param>
        /// <param name="response"></param>
        /// <returns></returns>
        public async Task ProcessNotificationAsync(
            HttpRequest request,
            HttpResponse response)
        {
            try
            {
                var httpRequest = request.CreateRequestMessage();
                var results = await this.AuthenticationProvider.ValidateInboundRequestAsync(httpRequest).ConfigureAwait(false);
                if (results.IsValid)
                {
                    var httpResponse = await this.NotificationProcessor.ProcessNotificationAsync(httpRequest).ConfigureAwait(false);
                    await httpResponse.CreateHttpResponseAsync(response).ConfigureAwait(false);
                }
                else
                {
                    var httpResponse = httpRequest.CreateResponse(HttpStatusCode.Forbidden);
                    await httpResponse.CreateHttpResponseAsync(response).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await response.WriteAsync(e.ToString()).ConfigureAwait(false);
            }
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            var credentials = new MicrosoftAppCredentials(this.configuration[Common.Constants.MicrosoftAppIdConfigurationSettingsKey], this.configuration[Common.Constants.MicrosoftAppPasswordConfigurationSettingsKey]);
            ConversationReference conversationReference = null;
            foreach (var member in membersAdded)
            {

                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    var proactiveMessage = MessageFactory.Attachment(this.card.GetWelcomeCardAttachment());
                    proactiveMessage.TeamsNotifyUser();
                    var conversationParameters = new ConversationParameters
                    {
                        IsGroup = false,
                        Bot = turnContext.Activity.Recipient,
                        Members = new ChannelAccount[] { member },
                        TenantId = turnContext.Activity.Conversation.TenantId
                    };
                    await ((BotFrameworkAdapter)turnContext.Adapter).CreateConversationAsync(
                        turnContext.Activity.TeamsGetChannelId(),
                        turnContext.Activity.ServiceUrl,
                        credentials,
                        conversationParameters,
                        async (t1, c1) =>
                        {
                            conversationReference = t1.Activity.GetConversationReference();
                            await ((BotFrameworkAdapter)turnContext.Adapter).ContinueConversationAsync(
                                configuration[Common.Constants.MicrosoftAppIdConfigurationSettingsKey],
                                conversationReference,
                                async (t2, c2) =>
                                {
                                    await t2.SendActivityAsync(proactiveMessage, c2);
                                },
                                cancellationToken);
                        },
                        cancellationToken);
                }
            }
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(turnContext.Activity.Text))
            {
                dynamic value = turnContext.Activity.Value;
                if (value != null)
                {
                    string type = value["type"];
                    type = string.IsNullOrEmpty(type) ? "." : type.ToLower();
                    System.Console.WriteLine("DAS IST TYPE " + type);
                    await SendReponse(turnContext, type, cancellationToken);
                }
            }
            else
            {
                await SendReponse(turnContext, turnContext.Activity.Text.Trim().ToLower(), cancellationToken);
            }
        }

        private async Task SendReponse(ITurnContext<IMessageActivity> turnContext, string input, CancellationToken cancellationToken)
        {
            var senderId = turnContext.Activity.From.AadObjectId;
            var senderName = turnContext.Activity.From.Name;
            switch (input)
            {
                case "createcall":
                    var call = await graph.CreateCallAsync();
                    if (call != null)
                    {
                        await turnContext.SendActivityAsync("Placed a call Successfully.");
                    }
                    break;
                case "transfercall":
                    var sourceCallResponse = await graph.CreateCallAsync();
                    if (sourceCallResponse != null)
                    {
                        await turnContext.SendActivityAsync("Transferring the call!");
                        await graph.TransferCallAsync(sourceCallResponse.Id);
                    }
                    break;
                case "joinscheduledmeeting":
                    var onlineMeeting = await graph.CreateOnlineMeetingAsync();
                    if (onlineMeeting != null)
                    {
                        var statefullCall = await graph.JoinScheduledMeeting(onlineMeeting.JoinWebUrl);
                        if (statefullCall != null)
                        {
                            await turnContext.SendActivityAsync($"[Click here to Join the meeting]({onlineMeeting.JoinWebUrl})");
                        }
                    }
                    break;
                case "inviteparticipant":
                    var meeting = await graph.CreateOnlineMeetingAsync();
                    if (meeting != null)
                    {
                        var statefullCall = await graph.JoinScheduledMeeting(meeting.JoinWebUrl);
                        if (statefullCall != null)
                        {
                            graph.InviteParticipant(statefullCall.Id);
                            await turnContext.SendActivityAsync("Invited participant successfuly");
                            var organizer = meeting.Participants.Organizer;
                            var role = organizer.Role;
                            await turnContext.SendActivityAsync(organizer.Upn);
                            //await turnContext.SendActivityAsync(role + " role");
                            await turnContext.SendActivityAsync(organizer.Identity.User.Id);
                            var username = organizer.Identity.User.DisplayName;
                            if (username != null)
                            {                            
                                await turnContext.SendActivityAsync(username);
                            }

                        }
                    }
                    break;
                case "new":
                    System.Console.WriteLine("Creating new Meeting...");
                    var newOnlineMeeting = new OnlineMeeting
                    {
	                    StartDateTime = DateTimeOffset.Parse("2022-08-07T23:00:00.2444915+00:00"),
	                    EndDateTime = DateTimeOffset.Parse("2022-08-07T23:01:00.2444915+00:00"),
	                    Subject = "User Token Meeting"
                    };
                    var meeting1 = await graphServiceClient.Users["95ff13bf-1f12-45ce-84a2-5031ef044785"].OnlineMeetings.Request().AddAsync(newOnlineMeeting);

                    System.Console.WriteLine("1");
                    System.Console.WriteLine(meeting1.JoinWebUrl);
                    System.Console.WriteLine(meeting1.Participants);
                    System.Console.WriteLine(meeting1.Subject);
                    System.Console.WriteLine(meeting1.StartDateTime);
                    System.Console.WriteLine("2");
                    break;
                case "newc":
                    var newCall = new Call
                    {
	                    CallbackUri = "https://bot.contoso.com/callback",
	                    Targets = new List<InvitationParticipantInfo>()
	                    {
		                    new InvitationParticipantInfo
		                    {
			                    Identity = new IdentitySet
			                    {   
                                    User = new Identity
                                    {
                                        DisplayName = "Julian Joachim",
                                        Id = "95ff13bf-1f12-45ce-84a2-5031ef044785"
                                    }
			                    }
		                    }
	                    },
	                    RequestedModalities = new List<Modality>()
	                    {
		                    Modality.Audio
	                    },
	                    MediaConfig = new ServiceHostedMediaConfig
	                    {
	                    },
                        TenantId = "1ff8950e-9285-4c2e-80fc-5522c267a97e"
                    };

                    var callinfo = await graphServiceClient.Communications.Calls.Request().AddAsync(newCall);
                    System.Console.WriteLine("1");
                    System.Console.Write(callinfo);
                    System.Console.WriteLine("2");
                    break;
                case "newgc":
                    var newgc = new Call
                    {
	                    Direction = CallDirection.Outgoing,
	                    Subject = "Create a group call with service hosted media",
	                    CallbackUri = "https://bot.contoso.com/callback",
                        Source = new ParticipantInfo
	                    {
		                    Identity = new IdentitySet
		                    {
			                    Application = new Identity
			                    {
				                    DisplayName = "Newest Meeting Bot",
				                    Id = "944588c9-67a2-4ad4-b6c9-eb68e8b31a0a"
			                    }
		                    }
	                    },
	                    Targets = new List<InvitationParticipantInfo>()
	                    {
		                    new InvitationParticipantInfo
		                    {
			                    Identity = new IdentitySet
			                    {   
                                    User = new Identity
                                    {
                                        DisplayName = "Julian Joachim",
                                        Id = "95ff13bf-1f12-45ce-84a2-5031ef044785"
                                    }
			                    }
		                    },
                            new InvitationParticipantInfo
                            {
                                Identity = new IdentitySet
                                {
                                    User = new Identity
                                    {
                                        DisplayName = "Adele Vance",
                                        Id = "1e8dd5e7-2ab0-4ca0-b6e7-d388298b22b6"
                                    }
                                }
                            },
                            new InvitationParticipantInfo
                            {
                                Identity = new IdentitySet
                                {
                                    User = new Identity
                                    {
                                        DisplayName = "Lee Gu",
                                        Id = "6745a81d-d72b-4010-94de-0a50a01dcc5d"
                                    }
                                }
                            },
                            new InvitationParticipantInfo
                            {
                                Identity = new IdentitySet
                                {
                                    User = new Identity
                                    {
                                        DisplayName = "Miriam Graham",
                                        Id = "14874bcb-158f-443b-ae7d-001a2294ca70"
                                    }
                                }
                            },
                            new InvitationParticipantInfo
                            {
                                Identity = new IdentitySet
                                {
                                    User = new Identity
                                    {
                                        DisplayName = "Pradeep Gupta",
                                        Id = "a04ccb53-5f49-4f27-85de-4c6c0d89bfbf"
                                    }
                                }
                            },
                            new InvitationParticipantInfo
                            {
                                Identity = new IdentitySet
                                {
                                    User = new Identity
                                    {
                                        DisplayName = "Henrietta Mueller",
                                        Id = "1ad1d8cb-78e4-4aff-9bbb-97759ee1a761"
                                    }
                                }
                            }
	                    },
	                    RequestedModalities = new List<Modality>()
	                    {
		                    Modality.Audio
	                    },
	                    MediaConfig = new ServiceHostedMediaConfig
	                    {
	                    },
                        TenantId = "1ff8950e-9285-4c2e-80fc-5522c267a97e"
                    };

                    System.Console.WriteLine(newgc);
                    System.Console.WriteLine(newgc.Targets);

                    newgc.Targets = getParticipants();

                    foreach (var parti in newgc.Targets)
                    {
                        System.Console.WriteLine(parti.Identity.User.DisplayName);
                    }
                    
                    var gcinfo = await graphServiceClient.Communications.Calls.Request().AddAsync(newgc);
                    System.Console.WriteLine("1");
                    System.Console.Write(gcinfo);
                    System.Console.WriteLine("2");
                    break;
                case "reportsick":
                    runSQL("UPDATE Employee SET attends = 0 WHERE id = '"+senderId+"';");
                    // CHECK IF SUCCESS
                    await turnContext.SendActivityAsync("Okay, " + senderName + ", du wurdest für das nächste Meeting ausgetragen. Sollte sich dein Plan ändern, benutzte gerne 'checkin' um dich wieder einzutragen. Andernfalls würden wir uns freuen wenn du einen kleinen schriftlichen Scrumbeitrag abgibst! Auf einen guten Arbeitstag.");
                    break;        
                case "register":
                    try
                    {
                        runSQL("INSERT INTO Employee (DisplayName, ID, attends) VALUES ('"+senderName+"', '"+senderId+"', '1');");

                        await turnContext.SendActivityAsync("Hallo " + senderName + "! Deine Registrierung war erfolgreich. :)");
                    }
                    catch (SqlException e)
                    {
                        Console.WriteLine(e.ToString());
                        await turnContext.SendActivityAsync("User bereits registriert.");

                    } 

                    // CHECK IF SUCCESS
                    break;
                case "helloworld":
                    System.Console.WriteLine("Hello World!");
                    break;
                case "help":
                    var helpCard = MessageFactory.Attachment(this.card.GetInfoCardAttachment());
                    await turnContext.SendActivityAsync(helpCard);
                    break;
                case "report":
                    var reportCard = MessageFactory.Attachment(this.card.GetReportCardAttachment());
                    await turnContext.SendActivityAsync(reportCard);
                    break;
                default:
                    await turnContext.SendActivityAsync("Welcome to bot. This is what you said: " + input);
                    break;
            }
        }


        private SqlConnectionStringBuilder getBuilder(){
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = "mysqlserverforteamsscrum.database.windows.net";
            builder.UserID = "azureuser";
            builder.Password = "Dov83bc20o2!b5yi78";
            builder.InitialCatalog = "scrumDB";
            return builder;
        }
        private IEnumerable<InvitationParticipantInfo> getParticipants(){
            var targetList = new List<InvitationParticipantInfo>();
            try
            {
                using (SqlConnection connection = new SqlConnection(getBuilder().ConnectionString))
                {
                    using (SqlCommand command = new SqlCommand("SELECT id, displayname, attends FROM Employee;", connection))
                    {
                        connection.Open();
                        using (SqlDataReader reader = command.ExecuteReader())
                        {                            
                            while (reader.Read())
                            {
                                if(reader.GetBoolean(2))
                                {
                                    targetList.Add(
                                    new InvitationParticipantInfo
                                    {
                                        Identity = new IdentitySet
                                        {
                                            User = new Identity
                                            {
                                                DisplayName = reader.GetString(1),
                                                Id = reader.GetString(0)
                                            }
                                        }
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch (SqlException e)
            {
                Console.WriteLine(e.ToString());
            }          
            return targetList;
        }
        private void runSQL(string sql){
                using (SqlConnection connection = new SqlConnection(getBuilder().ConnectionString))
                {
                    Console.WriteLine("\nQuery data example:");
                    Console.WriteLine("=========================================\n");

                    using (SqlCommand command = new SqlCommand(sql, connection))
                    {
                        connection.Open();
                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                Console.WriteLine("{0} {1}", reader.GetString(0), reader.GetString(1));
                                Console.WriteLine("{0}", reader.GetString(0));
                            }
                        }
                    }
                }
            Console.ReadLine();
        }

        private void NotificationProcessor_OnNotificationReceived(NotificationEventArgs args)
        {
            _ = NotificationProcessor_OnNotificationReceivedAsync(args).ForgetAndLogExceptionAsync(
              this.GraphLogger,
              $"Error processing notification {args.Notification.ResourceUrl} with scenario {args.ScenarioId}");
        }

        private async Task NotificationProcessor_OnNotificationReceivedAsync(NotificationEventArgs args)
        {
            this.GraphLogger.CorrelationId = args.ScenarioId;
            if (args.ResourceData is Call call)
            {
                if (args.ChangeType == ChangeType.Created && call.State == CallState.Incoming)
                {
                    await this.BotAnswerIncomingCallAsync(call.Id, args.TenantId, args.ScenarioId).ConfigureAwait(false);
                }
            }

        }

        private async Task BotAnswerIncomingCallAsync(string callId, string tenantId, Guid scenarioId)
        {

            Task answerTask = Task.Run(async () =>
                                await this.graphServiceClient.Communications.Calls[callId].Answer(
                                    callbackUri: new Uri(options.BotBaseUrl, "callback").ToString(),
                                    mediaConfig: new ServiceHostedMediaConfig
                                    {
                                        PreFetchMedia = new List<MediaInfo>()
                                        {
                                            new MediaInfo()
                                            {
                                                Uri = new Uri(options.BotBaseUrl, "audio/speech.wav").ToString(),
                                                ResourceId = Guid.NewGuid().ToString(),
                                            }
                                        }
                                    },
                                    acceptedModalities: new List<Modality> { Modality.Audio }).Request().PostAsync()
                                 );

            await answerTask.ContinueWith(async (antecedent) =>
            {

                if (antecedent.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    await Task.Delay(5000);
                    await graphServiceClient.Communications.Calls[callId].PlayPrompt(
                       prompts: new List<Microsoft.Graph.Prompt>()
                       {
                           new MediaPrompt
                           {
                               MediaInfo = new MediaInfo
                               {
                                   Uri = new Uri(options.BotBaseUrl, "audio/speech.wav").ToString(),
                                   ResourceId = Guid.NewGuid().ToString(),
                               }
                           }
                       })
                       .Request()
                       .PostAsync();
                }
            }
          );
        }
    }
}

