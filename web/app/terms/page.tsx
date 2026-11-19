import type { Metadata } from "next";
import { LegalPage } from "@/components/LegalPage";
import { site } from "@/content/site";

export const metadata: Metadata = {
  title: "Terms of Use",
  description: "The terms for using the FlyyLink apps for Android, macOS and Windows.",
};

export default function TermsPage() {
  return (
    <LegalPage title="Terms of Use">
      <p>
        These terms apply to the FlyyLink apps for Android, macOS and Windows (&quot;FlyyLink&quot;), made by{" "}
        {site.legalEntity} (&quot;we&quot;, &quot;us&quot;). By installing or using FlyyLink you agree to them. If you do
        not agree, please do not use FlyyLink.
      </p>

      <h2>1. The software</h2>
      <p>
        FlyyLink is owned by {site.legalEntity}. We grant you a personal, non-transferable license to install and use
        the apps we publish, including through Google Play. You may not copy, modify, resell or redistribute them, or
        reverse engineer them except where the law allows it.
      </p>

      <h2>2. Buying FlyyLink</h2>
      <p>
        The Android app is free. The Mac and Windows apps need a licence: a monthly or annual subscription, or a
        lifetime licence bought once. A subscription can start with a free trial. Unless you cancel before the trial
        ends, it becomes a paid subscription and you are charged then.
      </p>
      <ul>
        <li>
          Our order process is conducted by our online reseller Paddle.com. Paddle.com is the Merchant of Record for
          all our orders. Paddle provides all customer service inquiries and handles returns.
        </li>
        <li>
          A licence is for one person. It covers that person&apos;s own computers, up to 5 at a time, and includes
          future updates. You can swap computers on the <a href="/account">account page</a> up to 3 times a month.
        </li>
        <li>Do not share, sell or transfer your licence, or let others sign in with it.</li>
        <li>
          Subscriptions renew automatically at the end of each month or year until you cancel. Cancel any time on the{" "}
          <a href="/account">account page</a>; your computers keep working until the end of the period you paid for.
        </li>
        <li>
          If you are not happy, ask for a refund within 14 days of buying. A refunded licence stops working on your
          computers.
        </li>
        <li>
          The computer apps check your licence about once a day. They keep working offline for 37 days, then need to
          go online once to continue.
        </li>
      </ul>

      <h2>3. Using FlyyLink</h2>
      <ul>
        <li>Only pair devices that you own or are allowed to use.</li>
        <li>
          Do not use FlyyLink to access another person&apos;s messages, calls, notifications, location, photos or screen
          without their permission.
        </li>
        <li>Follow the laws that apply to you, including those on recording calls and sending messages.</li>
        <li>
          You are responsible for the content you send with FlyyLink and for any charges from your mobile carrier for
          texts and calls made through your phone.
        </li>
      </ul>

      <h2>4. Your data</h2>
      <p>
        FlyyLink moves information directly between your own devices. We do not receive it. We only keep what your
        licence needs. Our{" "}
        <a href="/privacy">Privacy Policy</a> explains this in full.
      </p>

      <h2>5. Other apps and services</h2>
      <p>
        FlyyLink can work with KDE Connect and relies on your operating system, your network and services such as
        Paddle, Cloudflare and map providers. We are not responsible for those, and their own terms apply.
      </p>

      <h2>6. Features that are still experimental</h2>
      <p>
        Some features, such as call audio on the Mac, are marked as experimental. They may not work on every device
        and may change or be removed.
      </p>

      <h2>7. No warranty</h2>
      <p>
        FlyyLink is provided &quot;as is&quot;, without warranties of any kind, express or implied, including fitness
        for a particular purpose and non-infringement. We do not promise that it will be uninterrupted, error free or
        compatible with every device. Keep your own backups of anything important before transferring it.
      </p>

      <h2>8. Limitation of liability</h2>
      <p>
        To the fullest extent the law allows, we are not liable for any indirect, incidental, special or consequential
        damages, or for any loss of data, messages, calls or profits, arising from your use of FlyyLink. Nothing in
        these terms limits liability that cannot be limited by law.
      </p>

      <h2>9. Ending use</h2>
      <p>You can stop using FlyyLink at any time by unpairing your devices and uninstalling the apps.</p>

      <h2>10. Changes</h2>
      <p>
        We may update these terms as FlyyLink changes. We will post the new version here with a new date. If you keep
        using FlyyLink after that, the new terms apply.
      </p>

      <h2>11. Governing law</h2>
      <p>These terms are governed by the laws of {site.jurisdiction}.</p>

      <h2>12. Contact</h2>
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
