import Foundation

final class SessionMetadata {
    typealias Update = ([String], [Bool]) -> Void
    typealias ControlsUpdate = ([String], [String], [String]) -> Void
    private struct Strip: Decodable {
        let kind: String
        let strip: Int
        let name: String
        let mono: Int
    }
    private let update: Update
    private let controlsUpdate: ControlsUpdate?
    private var timer: Timer?
    private let path: String

    init(path: String = "/Users/Shared/Waves/eMotion/Sessions/CurrentLV1.dat",
         controlsUpdate: ControlsUpdate? = nil,
         update: @escaping Update) {
        self.path = path
        self.controlsUpdate = controlsUpdate
        self.update = update
    }

    deinit { timer?.invalidate() }

    func start() {
        reload()
        timer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in self?.reload() }
    }

    func reload() {
        guard FileManager.default.fileExists(atPath: path) else { return }
        let process = Process()
        let output = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/sqlite3")
        // Read only, and use JSON so tabs, newlines and Unicode in channel names
        // cannot change the strip boundaries in the result.
        process.arguments = ["-readonly", "-json", path, Self.query]
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        do { try process.run() } catch { return }
        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0,
              let rows = try? JSONDecoder().decode([Strip].self, from: data) else { return }
        let strips = rows.filter { $0.kind == "strip" }
        let aux = rows.filter { $0.kind == "aux" }
        let users = rows.filter { $0.kind == "user" }
        let fx = rows.filter { $0.kind == "fx" }
        guard strips.count == 16, Set(strips.map(\.strip)) == Set(0..<16),
              aux.count == 24, Set(aux.map(\.strip)) == Set(0..<24),
              users.count == 16, Set(users.map(\.strip)) == Set(0..<16),
              fx.count == 8, Set(fx.map(\.strip)) == Set(0..<8) else { return }
        var names = Array(repeating: "—", count: 16), mono = Array(repeating: true, count: 16)
        for row in strips {
            names[row.strip] = row.name
            mono[row.strip] = row.mono == 1
        }
        update(names, mono)
        controlsUpdate?(aux.sorted { $0.strip < $1.strip }.map(\.name),
                        users.sorted { $0.strip < $1.strip }.map(\.name),
                        fx.sorted { $0.strip < $1.strip }.map(\.name))
    }

    // LV1 persists the visible Mixer 1 layer and page in surface 2.  Modes 4 and 5
    // are the factory Input and Bus pages; mode 1 is the user-defined Custom layer.
    // Resolve each visible strip to its actual LV1 object before reading its name and
    // channel format.  This deliberately never guesses stereo from meter activity.
    private static let query = """
    with recursive
      slots(strip) as (select 0 union all select strip+1 from slots where strip<15),
      aux_slots(strip) as (select 0 union all select strip+1 from aux_slots where strip<23),
      state(mode,page) as (
        select
          coalesce((select value from surface_property_int where surface=2 and property=12),4),
          coalesce((select value from surface_property_int where surface=2 and property=14),0)
      ),
      custom_layer(id) as (
        select min(id)+(select page from state) from ovv_layer where surface_type=2
      ),
      mapped(strip,obj_type,obj_index,obj_id) as (
        select s.strip,
          case
            when st.mode=4 and st.page between 0 and 4 then 0
            when st.mode=5 and st.page=0 then 1
            when st.mode=5 and st.page=1 and s.strip<8 then 2
            when st.mode=5 and st.page in (2,3) and (st.page=2 or s.strip<8) then 25
            when st.mode=5 and st.page=4 and s.strip=0 then 3
            when st.mode=5 and st.page=4 and s.strip=1 then 4
            when st.mode=5 and st.page=4 and s.strip=2 then 5
            when st.mode=5 and st.page=4 and s.strip=4 then 7
            when st.mode=5 and st.page=4 and s.strip=5 then 8
            when st.mode=5 and st.page=4 and s.strip between 8 and 15 then 6
            when st.mode=5 and st.page=5 then 17
          end,
          case
            when st.mode=4 and st.page between 0 and 4 then st.page*16+s.strip
            when st.mode=5 and st.page in (0,1,2,5) then s.strip
            when st.mode=5 and st.page=3 and s.strip<8 then 16+s.strip
            when st.mode=5 and st.page=4 and s.strip between 8 and 15 then s.strip-8
            when st.mode=5 and st.page=4 and s.strip in (0,1,2,4,5) then 0
          end,
          case when st.mode=1 then lt.obj_id end
        from slots s cross join state st
        left join ovv_layer_track lt
          on st.mode=1 and lt.layer_id=(select id from custom_layer) and lt.strip_index=s.strip
      ),
      resolved as (
        select m.strip,o.obj_type,o.obj_index,
          coalesce(nullif(active.name,''),o.name) as name,c.num_inputs
        from mapped m
        left join object o on o.id=m.obj_id or
          (m.obj_id is null and o.obj_type=m.obj_type and o.obj_index=m.obj_index)
        left join chainer c on c.obj_id=o.id
        -- -1 is LV1's live Active snapshot, including unsaved renames.
        -- object.name can remain empty while the visible channel name changes.
        -- Do not use the last recalled/saved scene's name.
        left join snapshot_chainer active on active.chainer_id=o.id and active.snapshot_id=-1
      )
    select 'strip' as kind,strip,
      case
        when obj_type is null or num_inputs<1 then '—'
        when nullif(name,'') is not null then name
        when obj_type=0 then 'Channel '||(obj_index+1)
        when obj_type=1 then 'Group '||(obj_index+1)
        when obj_type=2 then 'FX '||(obj_index+1)
        when obj_type=3 then 'LR'
        when obj_type=4 then 'Center'
        when obj_type=5 then 'Mono'
        when obj_type=6 then 'Matrix '||(obj_index+1)
        when obj_type=7 then 'Cue'
        when obj_type=8 then 'Talk Back'
        when obj_type=12 then 'DCA '||(obj_index+1)
        when obj_type=17 then 'Link '||(obj_index+1)
        when obj_type=25 then 'Mon '||(obj_index+1)
        else 'Bus '||(obj_index+1)
      end as name,
      case when num_inputs=2 then 0 else 1 end as mono
    from resolved
    union all
    select 'aux',s.strip,
      coalesce(nullif(active.name,''),nullif(o.name,''),'MON '||(s.strip+1)),0
    from aux_slots s
    left join object o on o.obj_type=25 and o.obj_index=s.strip
    left join snapshot_chainer active on active.chainer_id=o.id and active.snapshot_id=-1
    union all
    select 'user',s.strip,coalesce(nullif(k.uk_friendly_name,''),'USER '||(s.strip+1)),0
    from slots s
    left join user_keys k on k.key_index=s.strip
    union all
    select 'fx',s.strip,
      coalesce(nullif(active.name,''),nullif(o.name,''),'FX '||(s.strip+1)),0
    from slots s
    left join object o on o.obj_type=2 and o.obj_index=s.strip
    left join snapshot_chainer active on active.chainer_id=o.id and active.snapshot_id=-1
    where s.strip<8
    order by kind,strip;
    """
}
