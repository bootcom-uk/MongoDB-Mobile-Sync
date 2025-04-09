# Overview

**Project: MongoDB-Mobile-Sync**

With the deprecation of Atlas device sync, the intention of this project is to deliver something which is comparable. I use the free tier so want to continue utilising free or low cost solutions to deliver me the same results. The apps I use are for very low usage solutions. However, there are alternatives when you want to scale could include using changestreams for example but here we are using MongoDB App Services Triggers to listen to changes in MongoDB collections and forward inserts, updates, and deletions to app-specific endpoints based on configuration in the `SyncMappings` collection.

# What Does This Implementation Do? 

We've tried to take the bits that made Realm so great and easy to use.

# Steps To Take

## Step 1

### 🧩 MongoDB App Services Trigger

This trigger listens to changes in MongoDB collections and forwards inserts, updates, and deletions to app-specific endpoints based on configuration in the `SyncMappings` collection.
  Simply create a new trigger in your MongoDB App Services project to watch the database you're using (if there are multiple, create multiple triggers) and copy the code below into the trigger, remembering to set your actual cluster name (that's the only change I promise!)

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
        if (changeEvent.operationType === "update" && updatedFields) {
            let filteredDocument = {};
            matchingCollection.fields.forEach(field => {
                if (updatedFields[field] !== undefined) {
                    filteredDocument[field] = updatedFields[field];
                }
            });

            filteredDocument["_id"] = fullDocument._id;
          
            // If no relevant fields were updated, skip processing
            if (Object.keys(filteredDocument).length === 0) {
                console.log(`No relevant fields were updated for appId: ${appId}, database: ${databaseName}, and collection: ${collectionToUpdate}`);
                continue;
            }

            filteredDocument["__meta"] = { "dateUpdated": new Date() };

            await context.services.get(currentClusterName).db("AppServices").collection(collectionToUpdate).updateOne(
                { _id: fullDocument._id },
                { $set: filteredDocument },
                { upsert: true }
            );

            // Send updated data to the app's endpoint
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
        console.log(`Successfully notified endpoint ${endpoint}.`);
    }
}

```
</details>

## Step 2

### 🧩 MongoDB Web API

For implementing the sync we are relying on the bootcom-mongodb-sync-web library. Use the links below to access the page for this package on Nuget.

[![NuGet](https://img.shields.io/nuget/v/BootCom-MongoDB-Sync-Web.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-Web/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/BootCom-MongoDB-Sync-Web.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-Web/)


## Step 3



### 🧩 Configure Mappings 

## Step 4

### 🧩 Implement the sync in your app

For implementing the sync in your app, you can use the `BootCom-MongoDB-Sync-MAUI` library. This library provides a simple way to syncronize data from your local application to MongoDB and vice versa. Use the links below to access the page for this package on Nuget. 

[![NuGet](https://img.shields.io/nuget/v/BootCom-MongoDB-Sync-MAUI.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-MAUI/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/BootCom-MongoDB-Sync-MAUI.svg)](https://www.nuget.org/packages/BootCom-MongoDB-Sync-MAUI/)