import { ArrowRight, EyeOff, Lock, ServerOff } from "lucide-react";
import { DownloadButtons } from "@/components/DownloadButtons";
import { Showcase } from "@/components/Showcase";
import { features, platforms, steps } from "@/content/features";

const promises = [
  { icon: ServerOff, text: "Nothing goes through our servers" },
  { icon: EyeOff, text: "No ads, no tracking" },
  { icon: Lock, text: "Encrypted end to end" },
];

export default function Home() {
  return (
    <main>
      <section className="hero">
        <div className="container hero-grid">
          <div>
            <span className="eyebrow">For Android, Mac, Windows and TV</span>
            <h1>
              Android, <em>meet your computer.</em>
            </h1>
            <p className="lead">
              Your texts, calls, notifications, files and photos, right on your Mac or PC.
            </p>
            <DownloadButtons />
            <p className="hero-note">Free on Android. <a href="/buy">Try it free</a> on your Mac or PC.</p>
          </div>

          <div className="hero-visual" aria-hidden="true">
            <div className="gold-blob" />
            <img className="mac" src="/screens/mac-panel.png" alt="" />
            <img className="call" src="/screens/call-card.png" alt="" />
            <img className="phone" src="/screens/android-home.png" alt="" />
          </div>
        </div>
      </section>

      <section className="section" id="features">
        <div className="container">
          <div className="section-head">
            <span className="eyebrow">What it does</span>
            <h2>Put your phone down and still keep up.</h2>
          </div>

          <div className="features">
            {features.map(({ icon: Icon, title, body }) => (
              <div className="feature" key={title}>
                <div className="feature-icon">
                  <Icon size={22} />
                </div>
                <h3>{title}</h3>
                <p>{body}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section className="container">
        <Showcase
          eyebrow="Messages and calls"
          title="Text and talk from your computer."
          points={["Reply to texts from your keyboard", "Answer or decline calls", "Talk through your computer"]}
          media={
            <div className="stack">
              <img src="/screens/call-card.png" alt="An incoming call card on the Mac with answer and decline buttons" />
              <img src="/screens/messages.png" alt="The Messages window on the Mac, choosing which phone to text from" />
            </div>
          }
        />

        <Showcase
          reverse
          eyebrow="FlyyDrop"
          title="Share with anyone nearby."
          points={["Works with phones, Macs and PCs", "You decide who can send you things"]}
          media={<img className="bleed" src="/screens/flyydrop.png" alt="An Android phone being asked to accept three photos sent with FlyyDrop" />}
        />

        <Showcase
          gold
          eyebrow="All your devices"
          title="All your phones and computers, together."
          points={["Connect as many as you like", "Swipe to switch between them"]}
          media={<img className="tall" src="/screens/mac-panel.png" alt="The FlyyLink panel on a Mac with two phones paired" />}
        />

        <Showcase
          eyebrow="Screen mirroring"
          title="Use your phone from your computer."
          body="FlyyLink uses Android's AccessibilityService API to pass your clicks and typing to the phone. It's off until you agree and turn it on, works only while you mirror, and collects or shares no data."
          points={["Sound comes through too", "Use your mouse and keyboard"]}
          reverse
          media={<img className="tall" src="/screens/android-home.png" alt="FlyyLink on Android, showing a paired MacBook Pro" />}
        />

        <Showcase
          gold
          eyebrow="TV"
          title="Your phone is now the TV remote."
          points={["Use buttons or swipe on a touchpad", "Type searches with your keyboard", "Play photos, videos and music on any smart TV"]}
          media={<img className="bleed" src="/screens/tv-remote.png" alt="The FlyyLink TV remote on an Android phone, with a song playing on the living room TV" />}
        />
      </section>

      <section className="section" id="privacy">
        <div className="container">
          <div className="privacy-band">
            <div>
              <span className="eyebrow">Privacy</span>
              <h2>Your stuff stays yours.</h2>
            </div>

            <div className="pills">
              {promises.map(({ icon: Icon, text }) => (
                <div className="pill" key={text}>
                  <Icon size={22} />
                  {text}
                </div>
              ))}
            </div>
          </div>
        </div>
      </section>

      <section className="section" id="download">
        <div className="container">
          <div className="section-head">
            <span className="eyebrow">Get started</span>
            <h2>You'll be set up in a minute.</h2>
          </div>

          <div className="steps">
            {steps.map((step) => (
              <div className="step" key={step.title}>
                <h3>{step.title}</h3>
                <p>{step.body}</p>
              </div>
            ))}
          </div>

          <div className="platforms">
            {platforms.map((platform) => (
              <a className="platform" href={platform.href} key={platform.name}>
                <strong>{platform.name}</strong>
                <span>{platform.needs}</span>
                <span>{platform.detail}</span>
                {platform.playStore ? (
                  <img
                    className="play-badge"
                    src="https://play.google.com/intl/en_us/badges/static/images/badges/en_badge_web_generic.png"
                    alt="Get it on Google Play"
                  />
                ) : (
                  <span className="platform-cta">
                    Download <ArrowRight size={16} />
                  </span>
                )}
              </a>
            ))}
          </div>
        </div>
      </section>

      <section className="cta">
        <div className="container">
          <img src="/icon.svg" alt="" width={96} height={96} />
          <h2>Try it for free</h2>
          <DownloadButtons />
        </div>
      </section>
    </main>
  );
}
