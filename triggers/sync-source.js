/// <summary>Projects source changes into public caches or invalidates private collections.</summary>
exports = async function (changeEvent) {
    const currentClusterName = "<<ENTER CLUSTER NAME HERE>>";
    const operation = changeEvent.operationType;
    if (!["insert", "update", "replace", "delete"].includes(operation)) return;

    const database = changeEvent.ns.db;
    const collection = changeEvent.ns.coll;
    const service = context.services.get(currentClusterName);
    const cache = service.db("AppServices");
    const apps = await cache.collection("SyncMappings").find({
        collections: { $elemMatch: { databaseName: database, collectionName: collection } }
    }).toArray();
    let failures = 0;

    for (const app of apps) {
        try {
            const mapping = app.collections.find(c =>
                c.databaseName === database && c.collectionName === collection);
            if (!mapping) continue;
            if (!app.appId || !app.appName || !app.endpoint || !app.bearerToken)
                throw new Error("Incomplete application configuration.");

            const payload = { appId: app.appName, database, collection };
            // A corrupt policy must never fall back to broadcasting private records.
            if (mapping.filter != null && typeof mapping.filter !== "string")
                throw new Error("Invalid filter configuration.");
            if (typeof mapping.filter === "string" && mapping.filter.trim()) {
                await sendToWebAPI({ ...payload, action: "invalidate", document: {} }, app);
                continue;
            }

            const id = changeEvent.documentKey && changeEvent.documentKey._id;
            if (id == null) throw new Error("Missing source document ID.");
            const target = cache.collection(app.appId + "_" + collection);
            const document = { _id: id, __meta: { dateUpdated: new Date() } };

            if (operation === "delete") {
                document.__meta.deleted = true;
                // Replace the body with a tombstone; do not retain deleted private values.
                await target.replaceOne({ _id: id }, document, { upsert: true });
                await sendToWebAPI({ ...payload, action: "delete", document }, app);
                continue;
            }

            if (!changeEvent.fullDocument)
                throw new Error("Missing full document; enable Full Document on the trigger.");
            if (!Array.isArray(mapping.fields)) throw new Error("Invalid field configuration.");

            for (const field of mapping.fields) {
                if (field === "_id" || field === "__meta") continue;
                if (Object.prototype.hasOwnProperty.call(changeEvent.fullDocument, field))
                    document[field] = changeEvent.fullDocument[field];
            }

            // Replay-safe upsert, including inserts; removed source fields leave the cache.
            await target.replaceOne({ _id: id }, document, { upsert: true });

            // Legacy device handlers merge updates. Clear missing selected fields explicitly.
            // They remain null locally until a subsequent HTTP refresh replaces the row.
            const liveDocument = { ...document };
            for (const field of mapping.fields) {
                if (field !== "_id" && field !== "__meta" &&
                    !Object.prototype.hasOwnProperty.call(document, field))
                    liveDocument[field] = null;
            }
            await sendToWebAPI({ ...payload,
                action: operation === "insert" ? "insert" : "update",
                document: liveDocument
            }, app);
        } catch (_) {
            // Try the other applications; never log record bodies, tokens or response bodies.
            failures++;
        }
    }

    if (failures > 0)
        throw new Error(`Synchronization failed for ${failures} application(s). Check trigger configuration and API availability.`);
};

/// <summary>Sends typed JSON and rejects failed notifications without disclosing payloads.</summary>
async function sendToWebAPI(payload, app) {
    const response = await context.http.post({
        url: app.endpoint,
        headers: {
            "Content-Type": ["application/json"],
            "Authorization": [`Bearer ${app.bearerToken}`]
        },
        body: EJSON.stringify(payload, { relaxed: false })
    });
    if (!response || response.statusCode < 200 || response.statusCode > 299)
        throw new Error("Sync API rejected the notification.");
}
