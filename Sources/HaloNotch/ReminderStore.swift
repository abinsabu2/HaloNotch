import AppKit
import EventKit

/// Apple Reminders: listed in the Reminders tab; the notch opens when one with a due time is due.
/// ponytail: fires at the due time, not at custom alarm offsets; all-day reminders are skipped.
@MainActor
final class ReminderStore: ObservableObject {
    static let shared = ReminderStore()
    private static let enabledKey = "reminders.enabled"

    struct Due: Equatable, Identifiable {
        let id: String
        let title: String
        let date: Date?        // nil = no due date
        let hasTime: Bool      // false = all-day (listed, never opens the notch)
    }

    /// Incomplete reminders: dated ones first (soonest first), then undated.
    @Published private(set) var items: [Due] = []

    /// The reminder shown in the notch (one at a time; later ones wait in `queue`).
    @Published private(set) var current: Due?
    @Published private(set) var denied = false
    @Published var enabled = UserDefaults.standard.bool(forKey: ReminderStore.enabledKey) {
        didSet {
            guard enabled != oldValue else { return }
            UserDefaults.standard.set(enabled, forKey: Self.enabledKey)
            if enabled { start() } else { stop() }
        }
    }
    var onDue: (() -> Void)?

    private let store = EKEventStore()
    private var queue: [Due] = []
    private var since = Date()          // reminders due after this and up to now are shown
    private var timer: Timer?
    private var observers: [(NotificationCenter, NSObjectProtocol)] = []

    func start() {
        guard enabled, observers.isEmpty else { return }
        Task {
            let granted = (try? await store.requestFullAccessToReminders()) ?? false
            denied = !granted
            guard granted else { enabled = false; return }
            since = Date()
            let onChange: (Notification) -> Void = { [weak self] _ in Task { @MainActor in self?.reload() } }
            observers = [
                (NotificationCenter.default, NotificationCenter.default.addObserver(
                    forName: .EKEventStoreChanged, object: store, queue: .main, using: onChange)),
                (NSWorkspace.shared.notificationCenter, NSWorkspace.shared.notificationCenter.addObserver(
                    forName: NSWorkspace.didWakeNotification, object: nil, queue: .main, using: onChange)),
            ]
            reload()
        }
    }

    private func stop() {
        timer?.invalidate(); timer = nil
        observers.forEach { $0.0.removeObserver($0.1) }
        observers = []
        queue = []
        items = []
        current = nil
    }

    /// Marks the shown reminder completed in Apple Reminders.
    func complete() {
        if let id = current?.id { complete(id) }
        showNext()
    }

    /// Marks a reminder completed in Apple Reminders (the list reloads via EKEventStoreChanged).
    func complete(_ id: String) {
        guard let reminder = store.calendarItem(withIdentifier: id) as? EKReminder else { return }
        reminder.isCompleted = true
        try? store.save(reminder, commit: true)
    }

    func dismiss() { showNext() }

    private func showNext() {
        current = queue.isEmpty ? nil : queue.removeFirst()
        if current != nil { onDue?() }
    }

    private func reload() {
        guard enabled else { return }
        let predicate = store.predicateForIncompleteReminders(withDueDateStarting: nil, ending: nil, calendars: nil)
        store.fetchReminders(matching: predicate) { reminders in
            let items = (reminders ?? []).map { r in
                let c = r.dueDateComponents
                return Due(id: r.calendarItemIdentifier, title: r.title ?? "",
                           date: c.flatMap { Calendar.current.date(from: $0) }, hasTime: c?.hour != nil)
            }
            Task { @MainActor in self.schedule(items) }
        }
    }

    /// Queues reminders that became due since the last check and wakes up at the next due time.
    private func schedule(_ all: [Due]) {
        self.items = all.sorted { ($0.date ?? .distantFuture, $0.title) < ($1.date ?? .distantFuture, $1.title) }
        let now = Date()
        let timed = items.compactMap { item in item.hasTime ? item.date.map { (item, $0) } : nil }
        for (item, date) in timed
        where date > since && date <= now && item.id != current?.id && !queue.contains(where: { $0.id == item.id }) {
            queue.append(item)
        }
        since = max(since, now)
        if current == nil { showNext() }

        timer?.invalidate(); timer = nil
        guard let next = timed.map(\.1).filter({ $0 > now }).min() else { return }
        let t = Timer(fire: next, interval: 0, repeats: false) { [weak self] _ in
            Task { @MainActor in self?.reload() }
        }
        RunLoop.main.add(t, forMode: .common)
        timer = t
    }
}
