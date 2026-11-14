import SwiftUI
import FlyyLinkKit

/// "Play on TV": what plays on a TV with its controls, the TVs to pick from, or a button to choose a file.
/// Hidden while no TV is around.
struct PlayOnTvCard: View {
    @ObservedObject var model: PlayOnTvModel

    var body: some View {
        Group {
            if let session = model.session {
                NowPlayingOnTv(session: session, model: model)
            } else if let file = model.picked {
                TvPicker(file: file, model: model)
            } else if !model.tvs.isEmpty {
                start
            }
        }
        .animation(.spring(response: 0.35, dampingFraction: 0.85), value: model.session)
        .animation(.spring(response: 0.35, dampingFraction: 0.85), value: model.tvs)
    }

    private var start: some View {
        Button(action: model.choose) {
            HStack(spacing: 12) {
                TvChip(icon: .cast)

                VStack(alignment: .leading, spacing: 1) {
                    Text("Play on TV").font(.system(size: 13, weight: .semibold))
                    Text(model.error ?? "Photos, videos and music")
                        .font(.system(size: 11))
                        .foregroundStyle(model.error == nil ? Color.secondary : Theme.warn)
                        .lineLimit(1)
                }

                Spacer(minLength: 0)
            }
            .contentShape(Rectangle())
        }
        .buttonStyle(PressStyle())
        .card(padding: 10)
    }
}

private struct TvPicker: View {
    let file: MediaFile
    @ObservedObject var model: PlayOnTvModel

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 12) {
                TvChip(icon: kindIcon(CastKind.of(file.mime)))

                VStack(alignment: .leading, spacing: 1) {
                    Text(file.name).font(.system(size: 13, weight: .semibold)).lineLimit(1)
                    Text(model.searching && model.tvs.isEmpty ? "Looking for TVs…" : model.tvs.isEmpty ? "No TVs found" : "Pick a TV")
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                }

                Spacer(minLength: 0)

                if model.searching {
                    ProgressView().controlSize(.small)
                } else if model.tvs.isEmpty {
                    SmallButton(title: "Search again", action: model.search)
                }
                ControlButton(icon: .x, enabled: true, action: model.cancelPick)
            }

            ForEach(model.tvs) { tv in
                Button { model.play(on: tv) } label: {
                    HStack(spacing: 10) {
                        Image(lucide: .cast, size: 13).foregroundStyle(Theme.accent)
                        Text(tv.name).font(.system(size: 12, weight: .medium)).lineLimit(1)
                        Spacer(minLength: 0)
                        Image(lucide: .play, size: 11).foregroundStyle(.secondary)
                    }
                    .padding(.horizontal, 10)
                    .frame(height: 32)
                    .background(Theme.thumb, in: RoundedRectangle(cornerRadius: 8, style: .continuous))
                    .contentShape(Rectangle())
                }
                .buttonStyle(PressStyle())
            }
        }
        .card(padding: 10)
    }
}

private struct NowPlayingOnTv: View {
    let session: TvSession
    @ObservedObject var model: PlayOnTvModel

    var body: some View {
        VStack(spacing: 10) {
            HStack(spacing: 12) {
                TvChip(icon: kindIcon(session.kind))

                VStack(alignment: .leading, spacing: 1) {
                    Text(session.file.name).font(.system(size: 13, weight: .semibold)).lineLimit(1)
                    Text("On \(session.tv.name)").font(.system(size: 11)).foregroundStyle(.secondary).lineLimit(1)
                }

                Spacer(minLength: 0)

                HStack(spacing: 2) {
                    if session.state == .loading {
                        ProgressView().controlSize(.small).frame(width: 30, height: 30)
                    } else if session.kind != .photo {
                        ControlButton(icon: session.state == .paused ? .play : .pause, size: 17, enabled: true, action: model.togglePause)
                    }
                    ControlButton(icon: .x, enabled: true, action: model.stop)
                }
            }

            if session.duration > 0 {
                ProgressTrack(fraction: Double(session.position) / Double(session.duration), canSeek: true) { fraction in
                    model.seek(to: Int64(fraction * Double(session.duration)))
                }
            }
        }
        .card(padding: 12)
    }
}

private struct TvChip: View {
    let icon: Lucide

    var body: some View {
        RoundedRectangle(cornerRadius: 9, style: .continuous)
            .fill(Theme.tint)
            .frame(width: 36, height: 36)
            .overlay(Image(lucide: icon, size: 15, weight: .semibold).foregroundStyle(Theme.accent))
    }
}

private func kindIcon(_ kind: CastKind?) -> Lucide {
    switch kind {
    case .photo: return .image
    case .music: return .music
    default: return .film
    }
}
