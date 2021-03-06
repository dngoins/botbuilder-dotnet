﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Bot.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Bot.Builder.Core.Extensions
{
    public class StateSettings
    {
        public bool WriteBeforeSend { get; set; } = true;
        public bool LastWriterWins { get; set; } = true;
    }

    /// <summary>
    /// Abstract Base class which manages details of auto loading/saving of BotState
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public class BotState<TState> : IMiddleware
        where TState : class, new()
    {
        private readonly StateSettings _settings;
        private readonly IStorage _storage;
        private readonly Func<ITurnContext, string> _keyDelegate;
        private readonly string _propertyName;

        /// <summary>
        /// Create statemiddleware
        /// </summary>
        /// <param name="name">name of the kind of state</param>
        /// <param name="storage">storage provider to use</param>
        /// <param name="settings">settings</param>
        public BotState(IStorage storage, string propertyName, Func<ITurnContext, string> keyDelegate, StateSettings settings = null)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _propertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
            _keyDelegate = keyDelegate ?? throw new ArgumentNullException(nameof(keyDelegate));
            _settings = settings ?? new StateSettings();
        }

        public async Task OnTurn(ITurnContext context, MiddlewareSet.NextDelegate next)
        {
            await ReadToContextService(context).ConfigureAwait(false);
            await next().ConfigureAwait(false);
            await WriteFromContextService(context).ConfigureAwait(false);
        }

        protected virtual async Task ReadToContextService(ITurnContext context)
        {
            var key = this._keyDelegate(context);
            var items = await _storage.Read(new[] { key });
            var state = items.Where(entry => entry.Key == key).Select(entry => entry.Value).OfType<TState>().FirstOrDefault();
            if (state == null)
                state = new TState();
            context.Services.Add(this._propertyName, state);
        }

        protected virtual async Task WriteFromContextService(ITurnContext context)
        {
            var state = context.Services.Get<TState>(this._propertyName);
            await Write(context, state);
        }

        public virtual async Task<TState> Read(ITurnContext context)
        {
            var key = this._keyDelegate(context);
            var items = await _storage.Read(new[] { key });
            var state = items.Where(entry => entry.Key == key).Select(entry => entry.Value).OfType<TState>().FirstOrDefault();
            if (state == null)
                state = new TState();
            return state;
        }

        public virtual async Task Write(ITurnContext context, TState state)
        {
            var changes = new List<KeyValuePair<string, object>>();

            if (state == null)
                state = new TState();
            var key = _keyDelegate(context);
            changes.Add(new KeyValuePair<string, object>(key, state));

            if (this._settings.LastWriterWins)
            {
                foreach (var item in changes)
                {
                    if (item.Value is IStoreItem valueStoreItem)
                    {
                        valueStoreItem.eTag = "*";
                    }
                }
            }

            await _storage.Write(changes).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles persistence of StateT object using Context.Activity.Conversation.Id as the key
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public class ConversationState<TState> : BotState<TState>
        where TState : class, new()
    {
        public static string PropertyName = $"ConversationState:{typeof(ConversationState<TState>).Namespace}.{typeof(ConversationState<TState>).Name}";

        public ConversationState(IStorage storage, StateSettings settings = null) :
            base(storage, PropertyName,
                (context) => $"conversation/{context.Activity.ChannelId}/{context.Activity.Conversation.Id}",
                settings)
        {
        }

        /// <summary>
        /// get the value of the ConversationState from the context
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static TState Get(ITurnContext context) { return context.Services.Get<TState>(PropertyName); }
    }

    /// <summary>
    /// Handles persistence of StateT object using Context.Activity.From.Id (aka user id) as the key
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    public class UserState<TState> : BotState<TState>
        where TState : class, new()
    {
        public static readonly string PropertyName = $"UserState:{typeof(UserState<TState>).Namespace}.{typeof(UserState<TState>).Name}";

        public UserState(IStorage storage, StateSettings settings = null) :
            base(storage,
                PropertyName,
                (context) => $"user/{context.Activity.ChannelId}/{context.Activity.From.Id}")
        {
        }

        /// <summary>
        /// get the value of the ConversationState from the context
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static TState Get(ITurnContext context) { return context.Services.Get<TState>(PropertyName); }
    }

    public static class StateTurnContextExtensions
    {
        public static TState GetConversationState<TState>(this ITurnContext context)
            where TState : class, new()
        {
            return ConversationState<TState>.Get(context);
        }

        public static TState GetUserState<TState>(this ITurnContext context)
            where TState : class, new()
        {
            return UserState<TState>.Get(context);
        }
    }
}
