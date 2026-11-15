import type { Metadata } from "next";
import { LegalPage } from "@/components/LegalPage";
import { site } from "@/content/site";

export const metadata: Metadata = {
  title: "Privacy Policy",
  description: "How FlyyLink handles your data: on your devices, between your devices, and nowhere else.",
};

const permissions = [
  ["Notifications (notification access)", "Reads notifications on your phone so they can be shown, replied to, acted on or dismissed on your paired computer."],
  ["SMS (read and send)", "Shows your text conversations on your paired computer and sends the replies you write there."],
  ["Contacts", "Shows names instead of numbers for texts and calls on your paired computer."],
  ["Phone, call log and answering calls", "Tells your computer who is calling, lets you answer, decline or start calls from it, shows recent callers, and shows the network type (such as 4G) next to the signal."],
  ["Location, including in the background", "Used only for Find my phone. When you ask a paired computer to find your phone, the phone sends its current location to that computer so it can be shown on a map. This works even when FlyyLink is closed or not in use. FlyyLink does not track you, keep a location history, or send your location to us or anyone else, and it is never used for advertising. FlyyLink explains this and asks for your agreement before Android asks for location access."],
  ["Photos and videos", "Lets you browse recent photos and screenshots from your computer and copy the ones you pick."],
  ["Screen capture", "Streams your phone's screen to your paired computer when you start screen mirroring. Android asks for your consent every time."],
  ["Audio recording", "Captures the sound your phone is playing (not the microphone) while you mirror the screen, so you can hear it on your computer."],
  ["Accessibility service (\"FlyyLink computer control\")", "Lets the mouse and keyboard on your paired computer tap, swipe, type and press Back, Home and Recents on your phone, only while you mirror the screen. To type, it reads the text field you have selected on the phone and nothing else on your screen. FlyyLink does not collect, store or share any personal data through it: your clicks and typing travel only between your own paired devices. It is off until you agree to FlyyLink's explanation and turn it on in Android's settings, and it is used for nothing else."],
  ["Accessibility service on TVs (\"FlyyLink remote\")", "On a TV, lets your paired phone or computer move around the screen, move a pointer, select, press Back and Home, and type into the text field you have selected. It reads nothing else on screen. FlyyLink does not collect, store or share any personal data through it: your presses and typing travel only between your own paired devices. It is off until you agree to FlyyLink's explanation and turn it on in the TV's settings, and it is used for nothing else."],
  ["Display over other apps (TVs)", "On a TV, shows notifications and things your devices send as small cards in a corner of the screen, and opens them right away if you turn that on."],
  ["Bluetooth", "Finds your paired computer nearby and helps it connect when Wi-Fi is not available."],
  ["Wi-Fi, network and local hotspot", "Finds and connects to your paired devices on your local network, or through a temporary hotspot from your phone."],
  ["Files and clipboard", "Sends files, links and clipboard contents you choose to share to your paired devices, and saves the ones they send you."],
  ["Battery optimisation exemption and foreground service", "Keeps the connection alive in the background, shown as an ongoing notification."],
];

export default function PrivacyPage() {
  return (
    <LegalPage title="Privacy Policy">
      <div className="summary">
        <p>
          <strong>The short version:</strong> FlyyLink moves information between devices that you own and pair yourself.
          It does not send that information to us or to anyone else. If you buy FlyyLink, we keep your email address and
          the list of computers you use it on, so your licence works. No advertising and no analytics.
        </p>
      </div>

      <p>
        This policy explains how the FlyyLink apps for Android (phones, tablets and TVs), macOS and Windows (together, &quot;FlyyLink&quot;) handle
        information. FlyyLink is made by {site.legalEntity} (&quot;we&quot;, &quot;us&quot;). If you have a question,
        write to us at <a href={`mailto:${site.contactEmail}`}>{site.contactEmail}</a>.
      </p>

      <h2>1. How FlyyLink works</h2>
      <p>
        You install FlyyLink on your phone, computer or TV, and pair them by confirming the same code on both
        screens. After that, the paired devices connect to each other directly over your local network, over Bluetooth,
        or through a hotspot from your phone. Every connection is encrypted with TLS, using a certificate each device
        creates for itself. Devices you have not paired cannot read anything FlyyLink sends.
      </p>
      <p>
        With FlyyDrop you can also send files or links to a nearby device you have not paired. That device is asked
        first and receives only what its owner accepts. You choose whether others can FlyyDrop to you: only your
        paired devices (the default), everyone for 10 minutes, or everyone. To be found, every FlyyLink device shows its
        name to other FlyyLink devices on the same network.
      </p>
      <p>
        Nothing you send between your devices passes through servers run by us or by anyone else. The only account is
        the one for your licence, described in section 5.
      </p>

      <h2>2. Information FlyyLink accesses</h2>
      <p>
        FlyyLink only accesses information for the features you choose to use, and only after you grant the matching
        Android permission. You can turn any of them off at any time in FlyyLink or in your phone&apos;s settings.
      </p>
      <div className="table-scroll">
        <table>
          <thead>
            <tr>
              <th>Permission</th>
              <th>What it is used for</th>
            </tr>
          </thead>
          <tbody>
            {permissions.map(([name, use]) => (
              <tr key={name}>
                <td>{name}</td>
                <td>{use}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p>
        On a Mac or PC, FlyyLink reads the notifications your computer shows and sends them to your paired phones and
        computers. On a Mac this needs Full Disk Access, which you turn on in System Settings. Turn it off with
        &quot;Share this Mac&apos;s notifications&quot; or &quot;Share this PC&apos;s notifications&quot; in FlyyLink&apos;s settings.
      </p>

      <h2>3. Where your information goes</h2>
      <p>
        Information accessed through these permissions is sent only to the devices you have paired with FlyyLink, over
        an encrypted connection, so they can show it to you. It is not sent to us, sold, shared with third parties, or
        used for advertising, profiling or any purpose other than the feature you are using. When you FlyyDrop, only the
        files or links you pick go to the one device you choose, after its owner accepts them.
      </p>
      <p>
        With Play on TV, the photo, video or song you pick is streamed from your phone or computer straight to the TV you
        choose, over your local network. That TV does not need FlyyLink, and nothing else is sent to it.
      </p>
      <p>FlyyLink also speaks the open KDE Connect protocol, so you can pair it with KDE Connect on other devices. Anything you share with a device you pair is handled by the app on that device.</p>

      <h2>4. What is stored, and for how long</h2>
      <ul>
        <li>
          <strong>On every device:</strong> a device name and identifier, the encryption certificate, the list of
          devices you have paired, and your settings. These stay until you unpair a device or uninstall FlyyLink.
        </li>
        <li>
          <strong>Files and photos you send or download</strong> are saved where you can see them (for example your
          Downloads folder) and stay until you delete them.
        </li>
        <li>
          <strong>Notifications, messages, call details and location</strong> are shown on your computer while you
          use FlyyLink. The computer apps keep them in memory and in your operating system&apos;s notification centre,
          and do not upload them anywhere.
        </li>
      </ul>

      <h2>5. Your licence</h2>
      <p>The Android app is free and never contacts us. The Mac and Windows apps need a licence after a {site.trialDays}-day trial.</p>
      <ul>
        <li>
          <strong>Trial.</strong> When the trial starts, the computer app sends us a one-way scrambled code made from
          your computer&apos;s hardware ID, so the trial can&apos;t simply be restarted. We can&apos;t work out the
          hardware ID from it.
        </li>
        <li>
          <strong>Buying.</strong> Paddle.com sells FlyyLink for us as the merchant of record. Paddle handles your
          payment details, address and tax under{" "}
          <a href="https://www.paddle.com/legal/privacy">its own privacy policy</a>. We receive your email address and
          the order number, and keep them for as long as your licence exists.
        </li>
        <li>
          <strong>Signing in.</strong> To sign in on the website or a computer, we email you a short code, sent through
          SendPulse. The code expires after 10 minutes.
        </li>
        <li>
          <strong>Your computers.</strong> For each computer you sign in on, we keep its name, whether it is a Mac or
          PC, the scrambled hardware code, and when it was added and last checked its licence. The app checks in about
          once a day, which shares your IP address with Cloudflare, who host our website. You can see and remove your
          computers on the <a href="/account">account page</a>.
        </li>
      </ul>

      <h2>6. Services FlyyLink contacts on the internet</h2>
      <p>Apart from the licence, FlyyLink works without an internet connection. A few optional features contact outside services:</p>
      <ul>
        <li>
          <strong>Updates.</strong> The Mac and Windows apps check our website for new versions of FlyyLink. This
          shares your IP address and app version with Cloudflare, who host it. Android updates come through Google Play
          or the file you downloaded.
        </li>
        <li>
          <strong>Maps.</strong> When you look up your phone&apos;s location on your computer, the map is drawn by
          Apple Maps on a Mac and by OpenStreetMap on Windows. The map provider receives the area being shown so it
          can send map images. On Windows, opening the location in your browser uses Google Maps.
        </li>
        <li>
          <strong>Google Play.</strong> If you install FlyyLink from Google Play, Google handles the download and may
          collect information under its own privacy policy.
        </li>
      </ul>

      <h2>7. Children</h2>
      <p>
        FlyyLink is a general purpose tool and is not directed at children under 13. We do not knowingly collect
        information from children.
      </p>

      <h2>8. Your choices</h2>
      <ul>
        <li>Revoke any permission in your phone&apos;s settings. The matching feature simply stops working.</li>
        <li>Unpair a device in FlyyLink to stop it from connecting and remove its certificate.</li>
        <li>Uninstall FlyyLink to remove everything it stored on that device.</li>
      </ul>
      <p>
        To get a copy of your licence details or have them deleted, write to us from the email you bought with.
        If you have a question about your rights under laws such as the Nigeria Data Protection Act 2023, the GDPR or the CCPA, contact us and we will help.
      </p>

      <h2>9. Security</h2>
      <p>
        Connections between paired devices are encrypted and authenticated with each device&apos;s own certificate.
      </p>

      <h2>10. Changes to this policy</h2>
      <p>
        If we change how FlyyLink handles information, we will update this page and the date at the top. Significant
        changes will also be mentioned in the app&apos;s release notes.
      </p>

      <h2>11. Contact</h2>
      <p>
        {site.legalEntity}
        <br />
        {site.legalAddress}
        <br />
        <a href={`mailto:${site.contactEmail}`}>{site.contactEmail}</a>
      </p>
    </LegalPage>
  );
}
