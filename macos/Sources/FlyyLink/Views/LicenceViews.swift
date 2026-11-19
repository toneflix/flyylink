import SwiftUI

/// Signs in with an emailed code. While FlyyLink is locked it is the whole panel.
struct LicenceCard: View {
    @ObservedObject var licence: LicenceModel
    @FocusState private var focused: Bool

    var body: some View {
        VStack(spacing: 14) {
            Circle()
                .fill(Theme.tint)
                .frame(width: 56, height: 56)
                .overlay(Image(lucide: .lock, size: 24).foregroundStyle(Theme.accent))
                .frame(maxWidth: .infinity)
                .padding(.top, 6)

            VStack(spacing: 5) {
                Text(licence.title)
                    .font(.system(size: 17, weight: .bold))
                Text(licence.body)
                    .font(.system(size: 12))
                    .foregroundStyle(.secondary)
            }
            .multilineTextAlignment(.center)

            if licence.canRetry {
                Button("Try again") { licence.retry() }
                    .buttonStyle(PillStyle(prominent: true))
                    .disabled(licence.isBusy)
            }

            if licence.sentCode {
                Text("We sent a code to \(licence.email).")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
                TextField("Code", text: $licence.code)
                    .textFieldStyle(.roundedBorder)
                    .font(.system(size: 18, weight: .semibold, design: .monospaced))
                    .multilineTextAlignment(.center)
                    .focused($focused)
                    .onSubmit { licence.activate() }
                Button("Sign in") { licence.activate() }
                    .buttonStyle(PillStyle(prominent: true))
                    .disabled(licence.isBusy || licence.code.isEmpty)
                Button("Use another email") { licence.useAnotherEmail() }
                    .buttonStyle(.plain)
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.accent)
            } else {
                TextField("Email", text: $licence.email)
                    .textFieldStyle(.roundedBorder)
                    .focused($focused)
                    .onSubmit { licence.sendCode() }
                Button("Send code") { licence.sendCode() }
                    .buttonStyle(PillStyle(prominent: true))
                    .disabled(licence.isBusy || licence.email.isEmpty)
            }

            if licence.isBusy {
                ProgressView().controlSize(.small)
            }
            if let error = licence.error {
                Text(error)
                    .font(.system(size: 11.5))
                    .foregroundStyle(Theme.warn)
                    .multilineTextAlignment(.center)
            }

            Divider()
            HStack(spacing: 8) {
                Button(licence.buyTitle) { LicenceModel.buy() }
                    .buttonStyle(PillStyle(prominent: false))
                Button("Your computers") { LicenceModel.manageComputers() }
                    .buttonStyle(PillStyle(prominent: false))
            }
        }
        .card(padding: 20)
        .onAppear { focused = true }
        .animation(.easeOut(duration: 0.2), value: licence.sentCode)
    }
}
