# Overview

**Project: MongoDB-Mobile-Sync**

With the deprecation of Atlas device sync, the intention of this project is to deliver something which is comparable. I use the free tier so want to continue utilising free or low cost solutions to deliver me the same results. The apps I use are for very low usage solutions. However, there are alternatives when you want to scale could include using changestreams for example.

# Steps To Take

## Step 1

### Add a new database to your cluster

Database Name: **AppServices** 
Collection Name: **SyncMappings**

Don't worry too much at this stage about adding anything to it as we will address this as we get through

## Step 2

### Configure mappings



## Step 3

### Add a new trigger to MongoDB

``` json
exports = async function(changeEvent) {
  const fullDocument = changeEvent.fullDocument;
  const collectionName = changeEvent.ns.coll;

  // Get the mappings from the AppServices database
  const appServiceDb = context.services.get("mongodb-atlas").db("AppService");
  const mappingsCollection = appServiceDb.collection("Mappings");

  // Check if this collection is mapped for synchronization
  const mapping = await mappingsCollection.findOne({ collectionName: collectionName });
  
  if (!mapping) {
    // If the collection is not mapped, skip processing
    return;
  }

  // Filter the document to include only the mapped fields
  const filteredDocument = {};
  mapping.fields.forEach(field => {
    if (fullDocument.hasOwnProperty(field)) {
      filteredDocument[field] = fullDocument[field];
    }
  });

  // If no fields match the mapping, skip the update
  if (Object.keys(filteredDocument).length === 0) {
    return;
  }

  // Add the meta field with current date/time for tracking updates
  filteredDocument.__meta = { dateUpdated: new Date() };

  // Insert/Update the filtered document in the AppService database
  const appServiceCollection = appServiceDb.collection(collectionName);
  await appServiceCollection.updateOne(
    { _id: fullDocument._id },
    { $set: filteredDocument },
    { upsert: true }
  );

  // Send the change to your WebAPI endpoint
  const http = context.services.get("http");
  await http.post({
    url: "https://your-api-url/sync",
    body: { 
      collection: collectionName, 
      data: filteredDocument 
    },
    encodeBodyAsJSON: true
  });
};
```
