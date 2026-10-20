import Foundation
import Testing
@testable import LV1BridgeMac

struct SessionMetadataTests {
    private func execute(_ sql: String, at path: String) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/sqlite3")
        process.arguments = [path, sql]
        try process.run()
        process.waitUntilExit()
        #expect(process.terminationStatus == 0)
    }

    @Test func activeNamesRefreshAcrossRenamesBanksAndCustomLayers() throws {
        let path = FileManager.default.temporaryDirectory
            .appendingPathComponent("LV1BridgeMetadata-\(UUID().uuidString).db").path
        defer { try? FileManager.default.removeItem(atPath: path) }
        try execute("""
            create table object(id integer primary key, obj_type integer, obj_index integer, name text);
            create table chainer(obj_id integer primary key, num_inputs integer);
            create table snapshot_chainer(chainer_id integer, snapshot_id integer, name text);
            create table surface_property_int(surface integer, property integer, value integer);
            create table ovv_layer(id integer primary key, surface_type integer);
            create table ovv_layer_track(layer_id integer, strip_index integer, obj_id integer);
            create table user_keys(key_index integer primary key, uk_friendly_name text);
            insert into surface_property_int values(2,12,4),(2,14,0);
            insert into object values(10,0,0,''),(11,0,1,'Legacy name'),(26,0,16,''),(50,25,0,'');
            insert into chainer values(10,2),(11,1),(26,1),(50,2);
            insert into snapshot_chainer values
              (10,-1,'123'),(10,7,'Old saved name'),(11,-1,'Active vocal'),
              (26,-1,'Bank two'),(50,-1,'Singer monitor');
            insert into ovv_layer values(100,2);
            insert into ovv_layer_track values(100,0,26),(100,1,10);
            insert into user_keys values(0,'Talkback'),(15,'');
            """, at: path)
        var names: [String] = [], mono: [Bool] = []
        var aux: [String] = [], users: [String] = []
        let metadata = SessionMetadata(path: path, controlsUpdate: { aux = $0; users = $1 }) {
            names = $0; mono = $1
        }
        metadata.reload()
        #expect(names.count == 16)
        #expect(names[0] == "123")
        #expect(names[1] == "Active vocal")
        #expect(mono[0] == false && mono[1] == true)
        #expect(names[2] == "—")
        #expect(aux.count == 24 && aux[0] == "Singer monitor" && aux[23] == "MON 24")
        #expect(users.count == 16 && users[0] == "Talkback" && users[15] == "USER 16")

        // Change only the live name: the saved scene and channel layout stay put.
        let renamed = "שירה | Lead\t&\n\"Vocal\""
        try execute("update snapshot_chainer set name='\(renamed)' where chainer_id=10 and snapshot_id=-1;", at: path)
        metadata.reload()
        #expect(names[0] == renamed)
        #expect(mono[0] == false)
        try execute("""
            update snapshot_chainer set name='OHAD' where chainer_id=50 and snapshot_id=-1;
            update user_keys set uk_friendly_name='\(renamed)' where key_index=0;
            """, at: path)
        metadata.reload()
        #expect(aux[0] == "OHAD")
        #expect(users[0] == renamed)

        try execute("update surface_property_int set value=1 where surface=2 and property=14;", at: path)
        metadata.reload()
        #expect(names[0] == "Bank two")
        #expect(mono[0] == true)

        try execute("""
            update surface_property_int set value=1 where surface=2 and property=12;
            update surface_property_int set value=0 where surface=2 and property=14;
            """, at: path)
        metadata.reload()
        #expect(names[0] == "Bank two" && names[1] == renamed)
        #expect(mono[0] == true && mono[1] == false)

        try execute("""
            update surface_property_int set value=5 where surface=2 and property=12;
            update surface_property_int set value=2 where surface=2 and property=14;
            """, at: path)
        metadata.reload()
        #expect(names[0] == "OHAD")
        #expect(mono[0] == false)
        #expect(aux[0] == "OHAD", "MON labels do not depend on the visible VU bank")
        try execute("""
            update snapshot_chainer set name='' where chainer_id=50 and snapshot_id=-1;
            update user_keys set uk_friendly_name='' where key_index=0;
            """, at: path)
        metadata.reload()
        #expect(aux[0] == "MON 1" && users[0] == "USER 1")
    }
}
