﻿using Altinn.App.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Altinn.App.Core.Features
{
    /// <summary>
    /// Interface for providing <see cref="AppLists"/>
    /// </summary>
    public interface IAppListsProvider
    {
        /// <summary>
        /// The id/name of the options this provider supports ie. land, fylker, kommuner.
        /// You can have as many providers as you like, but you should have only one per id. 
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets the <see cref="AppLists"/> based on the provided options id and key value pairs.
        /// </summary>
        /// <param name="language">Language code</param>
        /// <param name="keyValuePairs">Key/value pairs to control what options to get.
        /// When called from the app lists controller this will be the querystring key/value pairs.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
        Task<AppLists> GetAppListsAsync(string language, Dictionary<string, string> keyValuePairs);
    }
}
