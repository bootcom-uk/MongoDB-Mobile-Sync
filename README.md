
# Overview

With the deprecation of Atlas device sync, the intention of this project is to deliver something which is comparable. I use the free tier so want to continue utilising free or low cost solutions to deliver me the same results. The apps we use are for low usage solutions and that is what we have tested against. However, there are alternatives when you want to scale could include using changestreams for example but here we are using MongoDB App Services Triggers to listen to changes in MongoDB collections and forward inserts, updates, and deletions to app-specific endpoints based on configuration in the `SyncMappings` collection.

# What Does This Implementation Do? 

We've tried to take the bits that made Realm so great and easy to use and extend this out. There is a bit more configuration to do but we are trying to make this as easy as possible.

# Steps To Take

## Step 1

### 🧩 MongoDB App Services Trigger

This trigger listens to changes in MongoDB collections and forwards inserts, updates, and deletions to app-specific endpoints based on configuration in the `SyncMappings` collection.
  Simply create a new trigger in your MongoDB App Services project to watch the database you're using (if there are multiple, create multiple triggers) and copy the code below into the trigger, remembering to set your actual cluster name.

  **Where you don't have an authentication mechanism which requires a bearer token, you will need to modify that line in the trigger**

<details>
  <summary>
  Click to expand / view trigger code
  </summary>

``` json
    exports = async function (changeEvent) {
    const currentClusterName = "<<YOUR CLUSTER NAME HERE>>";
    const db = context.services.get(currentClusterName).db("AppServices");
    const appCollection = db.collection("SyncMappings");

    const fullDocument = changeEvent.fullDocument;
    const updatedFields = changeEvent.updateDescription ? changeEvent.updateDescription.updatedFields : null;
    const collectionName = changeEvent.ns.coll;
    const databaseName = changeEvent.ns.db; // Get the database name from the change event

    // Retrieve all apps that include the current databaseName and collectionName
    const apps = await appCollection.find({
        "collections.databaseName": databaseName,
        "collections.collectionName": collectionName
    }).toArray();

    if (apps.length === 0) {
        console.log(`No apps found with database: ${databaseName} and collection: ${collectionName}`);
        return;
    }

    for (const app of apps) {
        const appId = app.appId;
        const appName = app.appName;
        let collectionToUpdate = appId + "_" + collectionName;

        const matchingCollection = app.collections.find(c => c.databaseName === databaseName && c.collectionName === collectionName);
        const endpoint = app.endpoint;  // Get the endpoint from SyncMappings
        const bearerToken = app.bearerToken; // Get the Bearer token from the app document

        if (!matchingCollection) {
            console.log(`No collection mapping found for appId: ${appId}, database: ${databaseName}, and collection: ${collectionName}`);
            continue;
        }

        // Handle document insertion
        if (changeEvent.operationType === "insert") {
            let insertedDocument = {};
            matchingCollection.fields.forEach(field => {
                if (fullDocument[field] !== undefined) {
                    insertedDocument[field] = fullDocument[field];
                }
            });

            if (Object.keys(insertedDocument).length > 0) {
                insertedDocument["__meta"] = { "dateUpdated": new Date() };

                await context.services.get(currentClusterName).db("AppServices").collection(collectionToUpdate).insertOne(insertedDocument);

                // Send data to the app's endpoint
                await sendToWebAPI({
                    action: "insert",
                    collection: collectionName,
                    document: insertedDocument,
                    appId: appName,
                    database: databaseName
                }, endpoint, bearerToken);
            }
        }

        // Handle document deletion
        if (changeEvent.operationType === "delete") {
            let filteredDocument = {};
            filteredDocument["__meta"] = { "dateUpdated": new Date(), "deleted": true };

            await context.services.get(currentClusterName).db("AppServices").collection(collectionToUpdate).updateOne(
                { _id: changeEvent.documentKey._id },
                { $set: filteredDocument },
                { upsert: true }
            );

            // Notify the app's endpoint about deletion
            await sendToWebAPI({
                action: "delete",
                collection: collectionName,
                document: { _id: changeEvent.documentKey._id, deleted: true },
                appId: appName,
                database: databaseName
            }, endpoint, bearerToken);
        }

        // Handle document update
        if (changeEvent.operationType === "update") {
          let filteredDocument = {};

          matchingCollection.fields.forEach(field => {
                if (fullDocument[field] !== undefined) {
                    filteredDocument[field] = fullDocument[field];
                }
            });
      
          // Always include _id
          filteredDocument["_id"] = fullDocument._id;
          filteredDocument["__meta"] = { "dateUpdated": new Date() };
      
          if (Object.keys(filteredDocument).length <= 2) { // _id and __meta don't count
              console.log(`No relevant fields were updated for appId: ${appId}, database: ${databaseName}, and collection: ${collectionToUpdate}`);
              continue;
          }
      
          await context.services.get(currentClusterName).db("AppServices").collection(collectionToUpdate).updateOne(
              { _id: fullDocument._id },
              { $set: filteredDocument },
              { upsert: true }
          );
      
          await sendToWebAPI({
              action: "update",
              collection: collectionName,
              document: filteredDocument,
              appId: appName,
              database: databaseName
          }, endpoint, bearerToken);
      }
    }
};

// Helper function to send HTTP POST request to the app's endpoint with Bearer Token
async function sendToWebAPI(payload, endpoint, bearerToken) {
    if (!endpoint || !bearerToken) {
        console.log(`Missing endpoint or Bearer token for payload: ${JSON.stringify(payload)}`);
        return;
    }

    const response = await context.http.post({
        url: endpoint,
        headers: {
            "Content-Type": ["application/json"],
            "Authorization": [`Bearer ${bearerToken}`] // Include Bearer Token in Authorization header
        },
        body: EJSON.stringify(payload, { relaxed: false }) // Preserve types
    });

    if (response.statusCode < 200 || response.statusCode > 299) {
        console.log(`Failed to notify endpoint ${endpoint}. Status code: ${response.statusCode}. Content: ${JSON.stringify(payload)}`);
    } else {
        console.log(`Successfully notified endpoint ${endpoint} of ${JSON.stringify(payload)}.`);
    }
}


```
</details>

## Step 2

### 🧩 MongoDB Web API

For implementing the sync we are relying on the bootcom-mongodb-sync-web library. Use the links below to access the page for this package on Nuget.

[![NuGet](https://img.shields.io/nuget/v/BootCom-MongoDB-Sync-Web.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-Web/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/BootCom-MongoDB-Sync-Web.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-Web/)

1. We need to ensure that we configure the various services.

<details>
  <summary>
  Click to expand / view service setup code
  </summary>  

  ``` csharp
  // Add services to the container.
builder.Services.AddSingleton<IMongoClient, MongoClient>(sp =>
{
    return new MongoClient(apiConfiguration!.MongoConfigurationSection.Connectionstring);
});

// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<BsonSchemaService>();
builder.Services.AddSingleton<IAppSyncService, AppSyncService>();
builder.Services.AddSingleton<InitialSyncService>();

// Add SignalR
builder.Services.AddSignalR()
    .AddAzureSignalR(options =>
    {
        options.ConnectionString = builder.Configuration["azure:SignalR:ConnectionString"];
        options.ServerStickyMode = Microsoft.Azure.SignalR.ServerStickyMode.Preferred;        
    });
```
  </details>

When we've mapped our controllers we then need to manually add the SignalR hub to the application pipeline.

``` csharp
app.MapHub<UpdateHub>("/hubs/update");
```

2. We now need to configure our first controller, this controller will deal with getting information about the app.

<details>
  <summary>
  Click to expand / view service app controller code
  </summary>  

``` csharp
public class AppController : BaseController
    {

        IAppSyncService _appSyncService;

        BsonSchemaService _schemaService;

        internal InitialSyncService _initialSyncService;

        private readonly IHubContext<UpdateHub> _updateHubContext;

        public AppController(ILogger<AppController> logger, IAppSyncService syncService, BsonSchemaService schemaService, IHubContext<UpdateHub> updateHubContext, InitialSyncService initialSyncService) : base(logger)
        {
            _appSyncService = syncService;
            _schemaService = schemaService;
            _updateHubContext = updateHubContext;
            _initialSyncService = initialSyncService;
        }

        /// <summary>
        /// Collects the schema for the MongoDB cluster
        /// </summary>
        /// <returns></returns>
        [HttpGet("schema")]
        public async Task<ActionResult> GetSchema()
        {
            return Ok(await _schemaService.GetFullDatabaseSchemaAsync());
        }

        /// <summary>
        /// Collects all applications for this MongoDB cluster
        /// </summary>
        /// <returns></returns>
        [HttpGet("all")]
        public async Task<ActionResult<IEnumerable<AppSyncMapping>>> GetApps()
        {
            try
            {
                var apps = await _appSyncService.GetAppSyncMappings();
                return Ok(apps);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting apps");
                return BadRequest(ex);
            }
        }

        [HttpPost]
        public async Task<ActionResult> Save(AppSyncMapping appSyncMapping)
        {
            try
            {
                var newAppSyncMapping = await _appSyncService.SaveAppSyncMapping(appSyncMapping);
                if(newAppSyncMapping != null)
                {
                    await _updateHubContext.Clients.Groups(appSyncMapping.AppId).SendAsync("AppSyncStarted");
                    await _initialSyncService.PerformInitialSync(appSyncMapping.AppName, appSyncMapping);
                    await _updateHubContext.Clients.Groups(appSyncMapping.AppId).SendAsync("AppSyncCompleted", newAppSyncMapping);
                }
                
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving the app sync mapping");
                return BadRequest(ex);
            }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(string id)
        {
            try
            {
                await _appSyncService.DeleteAppSyncMapping(id);
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting app");
                return BadRequest(ex);
            }
        }

    }
``` 
</details>

3. Our next controller that we need to setup is the DataSync controller. This controller will deal with the data sync process.

<details>
  <summary>
  Click to expand / view data sync controller code
  </summary>  
``` csharp
public class DataSyncController : BaseController
    {

        private readonly IAppSyncService _syncService;

        private readonly IHubContext<UpdateHub> _updateHubContext;

        public DataSyncController(IAppSyncService syncService, ILogger<DataSyncController> logger, IHubContext<UpdateHub> updateHubContext) : base(logger)
        {
            _syncService = syncService;
            _updateHubContext = updateHubContext;
        }

        [HttpPost("live-update")]
        public async Task<ActionResult> ReceiveLiveUpdate([FromBody] PayloadModel payloadModel)
        {
            try
            {
                 await _updateHubContext.Clients.Groups(payloadModel.AppId).SendAsync("ReceiveUpdate", JsonSerializer.Serialize(payloadModel));
                 //await _updateHubContext.Clients.All.SendAsync("ReceiveUpdate", JsonSerializer.Serialize(payloadModel));
                _logger.LogInformation($"Successfully sent document {JsonSerializer.Serialize(payloadModel)} via signalR");
            } catch(Exception ex)
            {
                return BadRequest(ex);
            }
            
            return NoContent();

        }

        [HttpPost("Send/{AppName}")]
        public async Task<ActionResult<Dictionary<string, string>>> SendDataToDatabase([FromRoute] string appName, [FromBody] LocalCacheDataChange localCacheDataChange)
        {



            var webLocalCacheDataChange = new WebLocalCacheDataChange() { Id = localCacheDataChange.Id, CollectionName = localCacheDataChange.CollectionName, IsDeletion = localCacheDataChange.IsDeletion, SerializedDocument = localCacheDataChange.SerializedDocument, Timestamp = localCacheDataChange.Timestamp };
            

            if (!string.IsNullOrWhiteSpace(webLocalCacheDataChange!.SerializedDocument))
            {
                webLocalCacheDataChange.Document = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(webLocalCacheDataChange.SerializedDocument);
            }
            var result = await _syncService.WriteDataToMongo(appName, webLocalCacheDataChange);

            if(result is null)
            {
                return BadRequest("An unhandled exception occurred");
            }

            if (result.ContainsKey("error"))
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpPost("Collect")]
        public async Task<ActionResult<IEnumerable<DatabaseAndCollection>>> GetAppInformation([FromForm(Name = "AppName")] string appName)
        {
            var data = await _syncService.GetAppInformation(appName);
            
            if (data == null) { return NotFound(); }

            return Ok(data);
        }

        [HttpPost("sync")]
        public async Task<ActionResult<SyncResult>> SyncData(
    [FromForm(Name = "AppName")] string appName,
    [FromForm(Name = "LastSyncDate")] DateTime? lastSyncDate,
    [FromForm(Name = "LastSyncedId")] string? lastSyncedId, // ID of the last synced document, 
    [FromForm(Name = "DatabaseName")] string databaseName,
    [FromForm(Name = "CollectionName")] string collectionName,
    [FromForm(Name = "PageNumber")] int pageNumber = 1)   // Page number to continue from
        {
            var userId = User.Claims.FirstOrDefault(record => record.Type == ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User not found.");
            }

            // Validate that the user has permission for the app (JWT-based check)
            if (!_syncService.UserHasPermission(appName, userId))
            {
                return Forbid("User does not have permission to sync this app.");
            }

            // Call the sync service to fetch data in batches
            var syncResult = await _syncService.SyncAppDataAsync(appName, userId, databaseName, collectionName, pageNumber, lastSyncedId, lastSyncDate);

            if (!syncResult.Success)
            {
                _logger.LogError($"Sync failed for app {appName}, user {userId}: {syncResult.ErrorMessage}");
                return StatusCode(500, "Sync failed.");
            }

            // Set the current page and collection name in the result
            syncResult.PageNumber = pageNumber; // Keep track of the page for the client to know the next batch
            syncResult.AppName = appName; // Include the app's collection name for clarity
            syncResult.DatabaseName = databaseName;

            return Ok(syncResult);
        }
    }
    ```
</details>

4. We now need the InitialSync controller. This controller will deal with the initial sync process.

<details>
  <summary>
  Click to expand / view initial sync controller code
  </summary>  

``` csharp
public class InitialSyncController : BaseController
    {

        internal InitialSyncService _initialSyncService;

        public InitialSyncController(InitialSyncService initialSyncService, ILogger<InitialSyncController> logger) : base(logger)
        {
            _initialSyncService = initialSyncService;
        }

        [HttpGet]
        [Description("Confirms whether the initial sync has now completed for this app")]
        public async Task<ActionResult<bool>> HasInitialSyncCompleted()
        {
            var audienceClaim = User.Claims.FirstOrDefault(record => record.Type == "aud");

            if (audienceClaim is null) { return NotFound(); }

            return Ok(await _initialSyncService.HasInitialSyncCompleted(audienceClaim.Value));
        }

        [Authorize(Policy = "IsAdministrator")]
        [HttpPost]
        public async Task<IActionResult> PerformInitialSync()
        {
            var audienceClaim = User.Claims.First(record => record.Type == "aud");

            await _initialSyncService.PerformInitialSync(audienceClaim.Value, null);

            return NoContent();

        }
    }
```

</details>

5. Finally, we need to setup the UpdateHub. This hub will be used to send updates to the client.

<details>
  <summary>
  Click to expand / view update hub code
  </summary>  

``` csharp
public class UpdateHub : Hub
    {

        ILogger<UpdateHub> _logger;

        public UpdateHub(ILogger<UpdateHub> logger) {
            _logger = logger;
        }

        public async Task SubscribeToApp(string appId)
        {
            _logger.LogInformation($"Client has subscribed to app {appId}");
            await Groups.AddToGroupAsync(Context.ConnectionId, appId);
        }

        public async Task UnsubscribeFromApp(string appId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, appId);
        }

        // Sends updates to the specific app's group
        public async Task SendUpdate(string appId, object update)
        {                        
            await Clients.Group(appId).SendAsync("ReceiveUpdate", JsonSerializer.Serialize(update));
            _logger.LogInformation($"Sending update to app {appId} with update {JsonSerializer.Serialize(update)}");
        }
    }
```

</details>

**Don't forget to decorate your controllers with the [Authorize] attribute if you are using authentication.**

## Step 3

### 🧩 Configure Mappings 

In order for us to syncronize data we need to configure the mappings. This is done by creating a new document in the `SyncMappings` collection within the `AppServices` database. The document should look like this:

<details>
  <summary>
  Click to expand / view sample collection
  </summary> 
``` json
{
  "appName": "BOOTCOM_HOME",
  "appDescription": "<<Describe your app>>",
  "appId": "<<Give a custom string id>>",
  "collections": [
    {
      "collectionName": "<<Collection Name>>",
      "databaseName": "<<Database Name>>",
      "fields": [
        "<<Field Name 1>>",
        "<<Field Name 2>>"
      ],
      "version": 1
    },
    {
      "collectionName": "<<Collection Name>>",
      "databaseName": "<<Database Name>>",
      "fields": [
        "<<Field Name 1>>",
        "<<Field Name 2>>"
      ],
      "version": 1
    }
  ],
  "_id": {
    "$oid": "<YOUR ID>"
  }
}
```
</details>

## Step 4

### 🧩 Implement the sync in your app

For implementing the sync in your app, you can use the `BootCom-MongoDB-Sync-MAUI` library. This library provides a simple way to syncronize data from your local application to MongoDB and vice versa. Use the links below to access the page for this package on Nuget. 

[![NuGet](https://img.shields.io/nuget/v/BootCom-MongoDB-Sync-MAUI.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-MAUI/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/BootCom-MongoDB-Sync-MAUI.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-MAUI/)

So, how do we set this up? Well very simply! In order to setup the sync service we need to add the following to the `MauiBuilder` in the `program.cs` file:

<details>

  <summary>
  Click to expand / view setup code
  </summary>  

  ``` csharp
  .SetupSyncService(options =>
            {

                options.ApiUrl = syncOptions.URL;
                options.AppName = syncOptions.AppName;
                options.LiteDbPath = Path.Combine(FileSystem.Current.AppDataDirectory, "home.db");
                options.PreRequestAction = async (request) =>
                {
                    var token = await internalSettingsService.GetSetting<string>(internalSettingsService.UserTokenSetting);
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                };
                options.StatusChangeAction = async (response) =>
                {

                    var userToken = await internalSettingsService.GetSetting<string>(internalSettingsService.UserTokenSetting);
                    var refreshToken = await internalSettingsService.GetSetting<string>(internalSettingsService.RefreshTokenSetting);
                    var deviceId = await internalSettingsService.GetSetting<Guid>(internalSettingsService.DeviceIdSetting);


                    var refreshTokenHttpBuilder = options.HttpService.CreateBuilder(new Uri(Endpoints.RefreshTokenUrl), HttpMethod.Post)
                        .WithHeader("Authorization", $"bearer {userToken}")
                        .WithFormContent(new()
                        {
                    { "deviceId", deviceId.ToString() },
                    { "refreshToken", refreshToken }
                        });

                    var refreshTokenResponse = await refreshTokenHttpBuilder
                        .SendAsync<Dictionary<string, string>>();

                    if (!refreshTokenResponse.Success)
                    {
                        return;
                    }

                    await internalSettingsService.SetSetting<string>(internalSettingsService.UserTokenSetting.SettingName, refreshTokenResponse.Result["JwtToken"]);
                    await internalSettingsService.SetSetting<string>(internalSettingsService.RefreshTokenSetting.SettingName, refreshTokenResponse.Result["RefreshToken"]);
                };
            })
```

  </details>

So we've now got the sync service configured, we're almost there. We now need to tell our application what data we're mapping to. How do we do this? Just create a POCO class and decorate it with the `CollectionName` attribute. This tells the sync service what the name of the collection is in our local cache store. Where you have a field that is a reference to another collection, this will be stored locally as an objectId but you can simply map it to the correct type in your POCO and it will automatically map to that field.


<details>
  <summary>
  Click to expand / view dto mapping code
  </summary>  

``` csharp
[CollectionName("bootcom_money_Payments")]
    public partial class Payment : ObservableObject
    {

        [ObservableProperty]
        ObjectId id;

        [ObservableProperty]
        DateTime startDate;

        [ObservableProperty]
        DateTime endDate;

        [ObservableProperty]
        string paymentTypeName;

        [ObservableProperty]
        string paymentTypeDescription;

        [ObservableProperty]
        PaymentType paymentTypeId;


    }
```

  </details>

  Finally, how can I access that data?

  We have built a simple collection type named: `LiveQueryableLiteCollection` which you can access as a function of the `LocalCacheService`. This collection will automatically be notified of any changes to data in the underlying local cache. The moment the data is modified in your MongoDB collection, the data will be updated in your local cache and will immediately notify your collection of the change so your UI will update in real time!

  Here is a simple example of how to use this:

``` csharp
 var collection = _localCacheService.GetLiveCollection<NewItemType>(_localCacheService.GetCollectionName<NewItemType>());
```

There is an overload here to specify your default filter for this, which can be useful if you want to filter and order the data in your app.

``` csharp
 var collection = _localCacheService.GetLiveCollection<Payment>(_localCacheService.GetCollectionName<Payment>(), (record => record.StartDate > new DateTime(2024, 6, 1)), payment => payment.OrderByDescending(record => record.StartDate));
```