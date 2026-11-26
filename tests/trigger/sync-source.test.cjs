const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const code = fs.readFileSync(path.join(root, 'triggers/sync-source.js'), 'utf8');

/// <summary>Executes the actual trigger against observable service doubles.</summary>
async function run(events, filter, statuses = [204], fields = ['name']) {
    const rows = new Map();
    const sent = [];
    const errors = [];
    const mappings = statuses.map((_, index) => ({ appId: 'app' + index, appName: 'APP' + index,
        endpoint: 'https://example.invalid', bearerToken: 'secret',
        collections: [{ databaseName: 'source', collectionName: 'records', fields, filter }] }));
    const sandbox = { EJSON: { stringify: (value, options) => {
        assert.equal(options.relaxed, false); return JSON.stringify(value);
    } }, context: {
        services: { get: name => {
            assert.equal(name, '<<ENTER CLUSTER NAME HERE>>');
            return { db: name => {
                assert.equal(name, 'AppServices');
                return { collection: name => name === 'SyncMappings' ? { find: query => {
                    assert.equal(query.collections.$elemMatch.databaseName, 'source');
                    return { toArray: async () => mappings };
                } } : { replaceOne: async (query, document, options) => {
                    assert.equal(query._id, 'source-id'); assert.equal(options.upsert, true);
                    rows.set(name, JSON.parse(JSON.stringify(document)));
                } } };
            } };
        } },
        http: { post: async request => {
            assert.equal(request.headers.Authorization[0], 'Bearer secret');
            const payload = JSON.parse(request.body); sent.push(payload);
            return { statusCode: statuses[Number(payload.appId.slice(3))] };
        } }
    } };
    vm.createContext(sandbox); vm.runInContext(code, sandbox);
    for (const event of events) {
        try { await sandbox.exports({ ns: { db: 'source', coll: 'records' },
            documentKey: { _id: 'source-id' }, ...event }); }
        catch (error) { errors.push(error.message); }
    }
    return { rows, sent, errors };
}

/// <summary>Checks private delivery, replay, removal, replacements and failure handling.</summary>
async function main() {
    const readme = fs.readFileSync(path.join(root, 'README.md'), 'utf8');
    assert.equal(readme.split('```javascript\n')[1].split('\n```')[0], code.trimEnd());
    const events = ['insert', 'update', 'replace', 'delete'].map(operationType => ({ operationType }));
    let result = await run(events, '{"$eq":["$owner","$$USER_ID"]}');
    assert.equal(result.rows.size, 0); assert.equal(result.sent.length, 4);
    assert.ok(result.sent.every(p => p.action === 'invalidate' && Object.keys(p.document).length === 0));
    assert.equal(result.errors.length, 0);
    result = await run([{ operationType: 'insert', fullDocument: { name: 'original' } },
        { operationType: 'insert', fullDocument: { name: 'original' } },
        { operationType: 'replace', fullDocument: {} }]);
    assert.equal(result.rows.size, 1);
    assert.equal(result.rows.get('app0_records')._id, 'source-id');
    assert.ok(!('name' in result.rows.get('app0_records')));
    assert.equal(result.sent[2].action, 'update');
    assert.equal(result.sent[2].document.name, null);
    result = await run([{ operationType: 'delete' }]);
    assert.equal(result.rows.get('app0_records').__meta.deleted, true);
    assert.deepEqual(Object.keys(result.rows.get('app0_records')).sort(), ['__meta', '_id']);
    result = await run([{ operationType: 'update' }]);
    assert.equal(result.errors.length, 1); assert.equal(result.rows.size, 0);
    result = await run(events, { invalid: true });
    assert.equal(result.sent.length, 0); assert.equal(result.rows.size, 0);
    result = await run([{ operationType: 'insert', fullDocument: { name: 'private-value' } }], null, [503, 204]);
    assert.equal(result.sent.length, 2); assert.equal(result.errors.length, 1);
    assert.ok(!result.errors[0].includes('private-value') && !result.errors[0].includes('secret'));
    result = await run([{ operationType: 'drop' }]);
    assert.equal(result.sent.length, 0); assert.equal(result.rows.size, 0);
    console.log('Trigger checks passed: README parity, private events, source IDs, replay, field removal, replacements, tombstones and failure isolation.');
}
main().catch(error => { console.error(error); process.exitCode = 1; });
