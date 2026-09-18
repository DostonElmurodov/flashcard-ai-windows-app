import { ArrowRight,BookOpen,Crown,GraduationCap } from 'lucide-react';

export default function Help({testMode=false,onCreate,onProfile,onSettings}:{testMode?:boolean;onCreate:()=>void;onProfile:()=>void;onSettings:()=>void}){
 return <div className="help-page">
  <section className="panel help-start">
   <div className="panel-title"><GraduationCap size={20}/><h2>Learn a little. Remember for longer.</h2></div>
   <p>Owl AI helps you remember words with flashcards. Try to recall a word, check the answer, and tell the app how easy it felt. Owl AI uses your answers to decide when to show that card again.</p>
   <ol className="help-steps">
    <li><strong>Create a set.</strong> Give it a name, choose your languages, and select <b>Save set</b>. A set is simply a collection of cards.</li>
    <li><strong>Add your words.</strong> Select your collection on Learn, then <b>Add cards</b>. Enter a word and its translation and select <b>Save cards</b>. The form stays open for your next word.</li>
    <li><strong>Practice on Learn.</strong> Select <b>Start today’s practice</b>. Think of the answer before selecting <b>Show answer</b>, then choose Again, Hard, Good, or Easy.</li>
   </ol>
   <p className="help-tip">Again = I forgot · Hard = I struggled · Good = I remembered · Easy = I knew it straight away.</p>
   <button className="button" onClick={onCreate}>Create a set <ArrowRight size={16}/></button>
  </section>
  <div className="help-columns">
   <section className="panel">
    <div className="panel-title"><BookOpen size={20}/><h2>What you can do</h2></div>
    <ul className="help-features">
     <li><strong>Create and study for free.</strong> Write cards, organize sets, and review saved cards offline. No account is needed for local study.</li>
     <li><strong>Bring in existing words.</strong> Paste text or import TXT, CSV, TSV, PDF, and image files. Check the preview, complete missing translations, then select Save selected cards.</li>
     <li><strong>Make cards useful to you.</strong> Edit words, translations, pronunciation, examples, and personal notes. Listen using the speaker button when a voice is available.</li>
     <li><strong>See your progress.</strong> Learn shows words collected, cards ready to review, cards practiced today, and your day streak.</li>
     <li><strong>Discover and share sets.</strong> Sign in with your existing iPhone account to use the online library. Imported sets are saved in your current workspace. Publish a set from its menu in My sets.</li>
     <li><strong>{testMode?'Get AI translations.':'Get AI translations with Premium.'}</strong> Choose AI translation when adding cards. {testMode?'An internet connection and a signed-in account are required.':'An internet connection and a signed-in account with Premium are required.'} Check the result before saving.</li>
     <li><strong>Make it comfortable.</strong> Set your languages, daily new-card goal, review direction, theme, and reminders in Preferences.</li>
    </ul>
   </section>
   {testMode?<section className="panel help-payment"><div className="panel-title"><GraduationCap size={20}/><h2>Test mode</h2></div><p>All learning features are available without a subscription while test mode is active. Word limits and AI quotas are lifted. Sign in to use online features and sync your cards.</p><button className="button secondary" onClick={onProfile}>Open Account <ArrowRight size={16}/></button></section>:<section className="panel help-payment">
    <div className="panel-title"><Crown size={20}/><h2>Premium & payment</h2></div>
    <p>You can keep creating and reviewing local cards for free. Premium adds online AI translation and can be shared between iPhone and Windows through your Owl AI account.</p>
    <ol className="help-steps">
     <li>Create or open your account in Owl AI on iPhone first. Windows supports signing in to that existing account.</li>
     <li>Open the Premium purchase screen. Review the available plan, price, and renewal terms, then confirm your purchase through Apple.</li>
     <li>In the iPhone app’s Profile, link your Apple purchase to your Owl AI account. If you already paid, restore your purchase when needed.</li>
     <li>Sign in to the same account on Windows. Open <b>Account & Premium</b> to see your status; it updates automatically while you are online.</li>
    </ol>
    <p className="help-tip">There is no separate Windows checkout yet. You do not need a second purchase for a linked, active iPhone subscription. Current prices are shown on the iPhone purchase screen.</p>
    <p>Manage or cancel an Apple subscription on your iPhone. Signing out of Owl AI or deleting your Owl AI account does not cancel Apple billing.</p>
    <button className="button secondary" onClick={onProfile}>Open Account & Premium <ArrowRight size={16}/></button>
   </section>}
  </div>
  <section className="panel help-faq">
   <h2>A few useful answers</h2>
   <details><summary>Where are my cards saved?</summary><p>When signed in, your collections, cards, and review schedule sync with the same Owl AI account on iPhone. Each account has its own workspace. Cards created while signed out stay in a separate local workspace and are never uploaded automatically. Sign out to return to those cards. In Preferences, use Save backup to keep a copy of your current workspace. You can also export an individual set as CSV from My sets.</p><button className="text-button" onClick={onSettings}>Open Preferences <ArrowRight size={16}/></button></details>
   <details><summary>How do I sign in and sync?</summary><p>Create or open your account in Owl AI on iPhone, then use the same email or Google account on Windows. Open {testMode?'Account':'Account & Premium'} to see synchronization status or select Sync now. Changes sync automatically when online. If the same card changed on both devices, sync preserves your local edits and reports a conflict instead of silently replacing them.</p><button className="text-button" onClick={onProfile}>Open {testMode?'Account':'Account & Premium'} <ArrowRight size={16}/></button></details>
   <details><summary>Why are there no cards ready to review?</summary><p>You may have finished today’s cards, reached your daily new-card goal, or selected different study languages. Check Preferences and the Active for study setting in My sets. Your only set is always active. With several sets, you can choose which ones to study.</p></details>
   <details><summary>What do the four numbers on Learn mean?</summary><p><b>Words collected</b> counts your saved cards. <b>Ready to review</b> counts cards available for the current session and study settings. <b>Practiced today</b> counts completed reviews, so practicing a card again counts again. <b>Day streak</b> tracks consecutive study days.</p></details>
   {!testMode&&<details><summary>I paid on iPhone. Why is Premium missing on Windows?</summary><p>Check that both apps use the same Owl AI account, that the Apple purchase is linked in the iPhone app, and that your subscription is still active. Connect to the internet and open Account & Premium on Windows. If your iPhone does not show the purchase, try Restore in its purchase screen.</p></details>}
   <details><summary>Are there keyboard shortcuts?</summary><p><b>Ctrl + F</b> opens word search on Learn. During practice, press <b>Space</b> to show the answer, then <b>1</b> for Again, <b>2</b> for Hard, <b>3</b> for Good, or <b>4</b> for Easy.</p></details>
  </section>
 </div>;
}
