use objc2::{
    define_class, msg_send, rc::Retained, runtime::{AnyObject, ProtocolObject}, sel, ClassType, MainThreadOnly
};
use objc2_app_kit::{NSApplication, NSWindow, NSWindowDelegate, NSBackingStoreType, NSWindowStyleMask, NSColor, NSAutoresizingMaskOptions, NSApplicationActivationPolicy};
use objc2_foundation::{MainThreadMarker, NSObject, NSObjectNSThreadPerformAdditions, NSObjectProtocol, NSPoint, NSRect, NSSize, NSString, NSNumber, NSURLRequest, NSURL};
#[allow(unused_imports)]
use objc2_web_kit::{WKNavigationDelegate, WKWebView, WKWebViewConfiguration};

define_class!(
    #[unsafe(super(NSObject))]
    #[thread_kind = MainThreadOnly]
    struct AppDelegate;

    unsafe impl NSObjectProtocol for AppDelegate {
    }
    unsafe impl NSWindowDelegate for AppDelegate {
    }

    impl AppDelegate {
        #[unsafe(method(windowWillClose:))]
        fn window_will_close(&self, _: *mut AnyObject) {
            unsafe {
                let mtm = MainThreadMarker::new().unwrap();

                let app = NSApplication::sharedApplication(mtm);
                app.terminate(None);
            }
        }
    }
);

pub fn open(url: String) {
    unsafe {
        let mtm = MainThreadMarker::new().unwrap();

        let app = NSApplication::sharedApplication(mtm);
        app.setActivationPolicy(NSApplicationActivationPolicy::Regular);

        let frame = NSRect::new(NSPoint::new(0., 0.), NSSize::new(1000., 700.));
        let window = NSWindow::initWithContentRect_styleMask_backing_defer(
            NSWindow::alloc(mtm),
            frame, NSWindowStyleMask::Titled | NSWindowStyleMask::Closable | NSWindowStyleMask::Miniaturizable | NSWindowStyleMask::Resizable,
            NSBackingStoreType::Buffered,
            false,
        );

        window.setMinSize(NSSize::new(500., 500.));
        window.setTitle(&NSString::from_str("Terracotta | 陶瓦联机"));

        let config = WKWebViewConfiguration::new(mtm);
        let webview = WKWebView::initWithFrame_configuration(WKWebView::alloc(mtm), frame, &config);

        let color = NSColor::colorWithRed_green_blue_alpha(0.102, 0.102, 0.18, 1.0);
        window.setBackgroundColor(Some(color.as_ref()));
        let _: () = msg_send![Retained::as_ptr(&window), setBackgroundColor: color.as_ref() as &NSColor];

        // webview.setBackgroundColor(Some(color.as_ref()));
        // webview.setHidden(Bool::YES);
        if webview.respondsToSelector(sel!(setUnderPageBackgroundColor:)) {
            webview.setUnderPageBackgroundColor(Some(color.as_ref()));
        } else {
            webview.setHidden(true);

            use std::sync::atomic::{AtomicPtr, Ordering::Acquire};

            let webview = AtomicPtr::new(Retained::as_ptr(&webview) as *mut WKWebView);
            std::thread::spawn(move || {
                let webview = Retained::from_raw(webview.load(Acquire)).unwrap();
                loop {
                    let title = webview.title();
                    if let Some(title) = title &&  objc2::rc::autoreleasepool(|pool| {
                        return NSString::to_str(&title, pool).contains("Terracotta");
                    }) {
                        std::thread::sleep(std::time::Duration::from_millis(200));

                        let webview = webview.downcast::<NSObject>().unwrap();
                        webview.performSelectorInBackground_withObject(sel!(setHidden:), None);
                        return;
                    }
                }
            });
        }

        let url_str = NSString::from_str(&url);
        webview.loadRequest(&NSURLRequest::requestWithURL(&NSURL::URLWithString(url_str.as_ref()).unwrap()));

        // Add observer for page load completion
        // let observer: *mut AnyObject = msg_send![WebviewObserver::class(), new];
        // let _: () = msg_send![webview, setNavigationDelegate: observer];

        window.contentView().unwrap().addSubview(webview.as_ref());
        webview.setAutoresizingMask(NSAutoresizingMaskOptions::ViewWidthSizable | NSAutoresizingMaskOptions::ViewHeightSizable);

        let delegate: *mut AnyObject = msg_send![AppDelegate::class(), new];
        let delegate = Retained::<AppDelegate>::from_raw(delegate as _).unwrap();
        let delegate = ProtocolObject::<dyn NSWindowDelegate>::from_retained(delegate);
        window.setDelegate(Some(delegate.as_ref()));
        window.makeKeyAndOrderFront(None);

        if app.respondsToSelector(sel!(activate)) {
            app.activate();
        } else {
            #[allow(deprecated)]
            app.activateIgnoringOtherApps(true);
        }

        app.run();
    }
}
