import AppKit
import SwiftUI
import FlyyLinkKit

/// The phone's photos and videos, newest first, grouped by month.
struct PhotosWindow: View {
    @ObservedObject var store: PhotosStore
    @ObservedObject var model: AppModel

    @State private var size: CGFloat = 132

    var body: some View {
        content
            .frame(minWidth: 640, minHeight: 460)
            .glassBackdrop()
            .navigationTitle("Photos")
            .navigationSubtitle(subtitle)
            .toolbar {
                ToolbarItem(placement: .navigation) {
                    PhonePicker(model: model)
                }
                ToolbarItemGroup {
                    Slider(value: $size, in: 88...220)
                        .frame(width: 110)
                        .help("Thumbnail size")
                    Button { if let id = store.selection.first { store.open(id) } } label: { Label("View", lucide: .eye) }
                        .keyboardShortcut(.space, modifiers: [])
                        .help("View the selected photo (Space)")
                        .disabled(store.selection.count != 1 || store.viewing != nil)
                    Button { store.refresh() } label: { Label("Refresh", lucide: .rotateCw) }
                        .help("Load the newest photos")
                        .disabled(!store.hasPhone)
                    Button { store.download(store.selection) } label: {
                        Label("Download", lucide: .download)
                    }
                    .help("Save the selected photos to Downloads")
                    .disabled(store.selection.isEmpty || !store.hasPhone)
                }
            }
            .overlay {
                if let photo = store.viewed {
                    PhotoViewer(store: store, photo: photo)
                        .transition(.opacity)
                }
            }
            .animation(.easeOut(duration: 0.18), value: store.viewing != nil)
            .onAppear { if store.photos.isEmpty { store.loadMore() } }
    }

    @ViewBuilder
    private var content: some View {
        if store.photos.isEmpty {
            VStack(spacing: 10) {
                Image(lucide: store.hasPhone ? .images : .unplug, size: 34, weight: .light)
                    .foregroundStyle(Theme.accent)
                Text(emptyText)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                if store.loading { ProgressView().controlSize(.small) }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            grid
        }
    }

    private var emptyText: String {
        if !store.hasPhone { return "Connect your phone to see its photos" }
        if store.loading { return "Loading your photos…" }
        return "No photos yet. Allow photo access in FlyyLink on your phone."
    }

    private var subtitle: String {
        if !store.selection.isEmpty { return "\(store.selection.count) selected" }
        return store.photos.isEmpty ? "" : "\(store.photos.count)\(store.more ? "+" : "") items"
    }

    private var grid: some View {
        ScrollView {
            LazyVGrid(
                columns: [GridItem(.adaptive(minimum: size, maximum: size * 1.4), spacing: 4)],
                spacing: 4,
                pinnedViews: [.sectionHeaders]
            ) {
                ForEach(store.months) { month in
                    Section {
                        ForEach(month.photos) { photo in
                            PhotoTile(
                                photo: photo,
                                image: store.thumbnails[photo.id],
                                isSelected: store.selection.contains(photo.id),
                                isDownloading: store.downloading.contains(photo.id)
                            )
                            .onTapGesture(count: 2) { store.open(photo.id) }
                            .onTapGesture { toggle(photo.id) }
                            .contextMenu {
                                Button("Open") { store.open(photo.id) }
                                Button("Save to Downloads") { store.download([photo.id]) }
                            }
                            .onAppear { if photo.id == store.photos.last?.id { store.loadMore() } }
                        }
                    } header: {
                        MonthHeader(title: month.title, count: month.photos.count)
                    }
                }

                if store.loading {
                    ProgressView().controlSize(.small).padding()
                }
            }
            .padding(.horizontal, 12)
            .padding(.bottom, 12)
        }
    }

    /// Click selects one; Command-click adds to the selection.
    private func toggle(_ id: Int64) {
        if NSEvent.modifierFlags.contains(.command) || NSEvent.modifierFlags.contains(.shift) {
            if store.selection.contains(id) { store.selection.remove(id) } else { store.selection.insert(id) }
        } else {
            store.selection = store.selection == [id] ? [] : [id]
        }
    }
}

private struct MonthHeader: View {
    let title: String
    let count: Int

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            Text(title).font(.system(size: 15, weight: .bold))
            Text("\(count)").font(.system(size: 12)).foregroundStyle(.secondary)
            Spacer()
        }
        .padding(.vertical, 8)
        .frame(maxWidth: .infinity)
        .background(.bar)
    }
}

/// One square thumbnail. Videos show a play badge; a downloading photo shows a spinner.
private struct PhotoTile: View {
    let photo: PhonePhoto
    let image: NSImage?
    let isSelected: Bool
    let isDownloading: Bool

    @State private var hovering = false

    var body: some View {
        Color.clear
            .aspectRatio(1, contentMode: .fit)
            .overlay {
                if let image {
                    Image(nsImage: image).resizable().scaledToFill()
                } else {
                    Theme.thumb.overlay(
                        Image(lucide: photo.isVideo ? .video : .image, size: 18).foregroundStyle(.secondary)
                    )
                }
            }
            .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
            .overlay(alignment: .bottomLeading) {
                if photo.isVideo {
                    Image(lucide: .play, size: 10, weight: .bold)
                        .foregroundStyle(.white)
                        .padding(5)
                        .background(.black.opacity(0.45), in: Circle())
                        .padding(6)
                }
            }
            .overlay(alignment: .topTrailing) {
                if isDownloading {
                    TileBadge(icon: .arrowDownToLine)
                } else if isSelected {
                    TileBadge(icon: .check)
                }
            }
            .overlay(
                RoundedRectangle(cornerRadius: 6, style: .continuous)
                    .stroke(Theme.accent, lineWidth: isSelected ? 3 : 0)
            )
            .brightness(hovering ? 0.05 : 0)
            .contentShape(Rectangle())
            .onHover { hovering = $0 }
            .help("\(photo.name)\n\(PhotosStore.date(photo).formatted(date: .abbreviated, time: .shortened))")
    }
}

/// A small accent circle in a thumbnail's corner, showing its state.
private struct TileBadge: View {
    let icon: Lucide

    var body: some View {
        Image(lucide: icon, size: 11, weight: .bold)
            .foregroundStyle(Theme.onAccent)
            .frame(width: 20, height: 20)
            .background(Theme.accent, in: Circle())
            .overlay(Circle().stroke(Theme.onAccent, lineWidth: 1.5))
            .padding(6)
    }
}
