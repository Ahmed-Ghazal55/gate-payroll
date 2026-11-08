export interface paths {
    "/app": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Serve the optional Next application entry point */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        /** Serve the optional Next application entry point */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/app/{path}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Serve extensionless Next client routes; unknown assets remain 404 */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    path: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        /** Serve extensionless Next client routes; unknown assets remain 404 */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    path: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/api/vouchers/verify/{no}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Public rate-limited voucher validity/status/type only; no personal or financial values */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    no: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/health": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read  health */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJobsHealthAndbooleanAndstringAndstringAndbooleanAndboolean"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read auth status */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndstring"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/setup": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit auth setup */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit auth login */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit auth logout */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/statement": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read own approved monthly statement using rider bearer session only */
        get: {
            parameters: {
                query: {
                    period: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/sessions/mine": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read own sessions using an active rider bearer token */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** End the rider's current bearer session */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfboolean"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/requests": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read own advance asks and their stage using the rider bearer session */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        /** Rider asks the current supervisor for a cash or bank advance */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/requests/{id}/cancel": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Rider cancels an open advance ask */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/device-token": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Update FCM device token for current mobile session */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndboolean"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit rider mobile login */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/broadcasts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read scoped broadcasts with a staff cookie or active rider bearer token */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/portal/{iqama}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read rider mobile portal {iqama} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonObjectAnddecimalAnddecimalAnddecimalAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstringAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndstring"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/broadcasts/{id}/ack": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit rider mobile broadcasts {id} ack */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndstringAndint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/rider-app": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read  rider app */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/rider-app.js": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read  rider app.js */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/questionnaires/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read public questionnaires {token} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        /** Submit public questionnaires {token} */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/questionnaires/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read  questionnaires {token} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/inquiry/payslip": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit public inquiry */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/inquiry/debt": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit public inquiry */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/inquiry": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read  inquiry */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/forms/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read public forms {token} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        /** Submit public forms {token} */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/forms/{token}/lookup": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit public forms {token} lookup */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/forms/{token}/resume": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Submit public forms {token} resume
         * @deprecated
         */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/public/forms/{token}/edit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit public forms {token} edit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/forms/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read  forms {token} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    token: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/rider-form.js": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read  rider form.js */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Redirect to the current administration app */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/legacy": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Serve legacy administration to a signed-in session */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List visible notifications with per-user read state */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Unread notification counts per category for the current user */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            [key: string]: number | string;
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/{id}/read": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Mark one visible notification read for the current user */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfboolean"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/read-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Mark all visible notifications read for the current user */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/erp-sync": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read administrator-only counts and refusal codes for recent ERP sync runs */
        get: {
            parameters: {
                query?: {
                    limit?: number | string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/jobs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read safe background job status */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JobView"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/jobs/erp-sync/run": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Queue permission-gated ERP sync with cooldown */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/jobs/erp-sync/dry-run": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Queue permission-gated counts-only ERP preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/jobs/erp-sync/{jobId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read counts-only ERP job progress */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    jobId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/jobs/erp-sync/pause": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Pause ERP scheduling with SuperAdmin reason */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfboolean"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/jobs/erp-sync/resume": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Resume approved ERP scheduling with SuperAdmin reason */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfboolean"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/config/lists": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read admin lists with fields and item versions */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/config/lists/{group}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read one admin list */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    group: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/config/lists/{group}/items": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Add or change a list item; financial lists wait for approval */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    group: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/config/lists/{group}/items/{code}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approve a pending financial list change by another user */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    group: string;
                    code: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/config/lists/{group}/fields": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Add a custom field to a list */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    group: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/imports/catalog": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Ordered import steps with template, preview and commit links and the required columns */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonArray"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/imports/templates/{name}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Download an empty import template from the catalog */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    name: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List companies within the current legal-entity scope */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        /** Create and audit a company */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/expiring": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List expired or soon-expiring company records and documents */
        get: {
            parameters: {
                query?: {
                    days?: number | string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read one company within scope */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        /** Update and audit company metadata */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        post?: never;
        /** Archive a company while preserving financial references */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/{id}/registrations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List commercial registrations for a company */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        /** Create and audit a commercial registration */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/{id}/registrations/{registrationId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read a company commercial registration */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    registrationId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        /** Update a commercial registration */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    registrationId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        post?: never;
        /** Archive a commercial registration */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    registrationId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/{id}/documents": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List company documents */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        /** Upload and audit a company document */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/{id}/documents/{documentId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read company document metadata */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    documentId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        /** Update a company document and retain prior files */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    documentId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        post?: never;
        /** Archive a company document without deleting its file */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    documentId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/{id}/documents/{documentId}/download": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Download an authorized company attachment */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    documentId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/catalog": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read departments, ranks A+/A/B/C and routed operations */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonArrayAndIEnumerableOfAnonymousTypeOfstringAndintAndString[]"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/memberships": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read dated department memberships and ranks */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        /** Add a dated membership with rank A+/A/B/C */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/memberships/{id}/end": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** End a membership without deleting history */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/delegations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read dated delegations */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        /** Add a delegation that expires on its end date */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/delegations/{id}/end": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** End a delegation early */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/supervision": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read supervisor-rider links; mine=1 lists the caller's active riders */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        /** Link a supervisor to a rider from a date */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/supervision/{id}/end": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** End a supervisor-rider link */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/routing": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read operation routing: department, minimum rank and named responsible users or custodians */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        /** Add a dated routing row; the newest active row wins */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/routing/{id}/end": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** End a routing row */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/org/responsible": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Resolve who is responsible for an operation today */
        get: {
            parameters: {
                query: {
                    operation: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List advance requests visible to the caller (role=mine|review|payout) */
        get: {
            parameters: {
                query?: {
                    role?: string;
                    status?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        /** Supervisor requests a cash or bank advance for a supervised rider */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read one advance request with its decision history */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/{id}/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Rider performance, debt and forecast summary plus vault balances for the reviewer */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/{id}/review": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Responsible accountant approves, rejects or proposes a change */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/{id}/respond": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Supervisor accepts or rejects the accountant's proposal */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/{id}/payout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Vault custodian pays out the approved advance; creates the debt and journal */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/terms/seed": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read the Ninja contract seed terms (draft source for a ninja_target pay_terms version) */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/reconcile": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Recalculate uploaded Ninja supplier invoices captain by captain against the contract terms (read-only) */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List scoped Ninja invoices */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                companyId: string;
                                platform?: string;
                                period: string;
                                reference?: string;
                                amountExclVat: number;
                                deductionsTotal: number;
                                vat: number;
                                approvalStatus: string;
                                status: string;
                                allowedActions: string[];
                                approvalBlockers?: string[];
                                journalEntryId?: string;
                                createdBy?: string;
                                approvedBy?: string;
                                accountingStatus?: string;
                                reversalJournalEntryId?: string;
                                disputes?: {
                                    id: string;
                                    status: string;
                                    reason: string;
                                    createdBy: string;
                                    resolvedBy?: string;
                                }[];
                                accountIds?: null | string[];
                                lines?: {
                                    id: string;
                                    invoiceId: string;
                                    sourceRow: number;
                                    platformAccountId?: string;
                                    captainId?: string;
                                    lineType?: string;
                                    baseGross: number;
                                    incentives: number;
                                    deductionAmount: number;
                                    orders: number;
                                    vat: number;
                                    expected?: number;
                                    invoiced: number;
                                    difference?: number;
                                    reason?: string;
                                    status: string;
                                    deductions?: {
                                        category: string;
                                        amount: number;
                                        level: string;
                                        expenseAccount?: string;
                                        riderId?: string;
                                    }[];
                                }[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Preview or confirm a signed Ninja XLSX invoice; invoice differences are informational */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        } | {
                            rows: {
                                id: string;
                                companyId: string;
                                platform?: string;
                                period: string;
                                reference?: string;
                                amountExclVat: number;
                                deductionsTotal: number;
                                vat: number;
                                approvalStatus: string;
                                status: string;
                                allowedActions: string[];
                                approvalBlockers?: string[];
                                journalEntryId?: string;
                                createdBy?: string;
                                approvedBy?: string;
                                accountingStatus?: string;
                                reversalJournalEntryId?: string;
                                disputes?: {
                                    id: string;
                                    status: string;
                                    reason: string;
                                    createdBy: string;
                                    resolvedBy?: string;
                                }[];
                                accountIds?: null | string[];
                                lines?: {
                                    id: string;
                                    invoiceId: string;
                                    sourceRow: number;
                                    platformAccountId?: string;
                                    captainId?: string;
                                    lineType?: string;
                                    baseGross: number;
                                    incentives: number;
                                    deductionAmount: number;
                                    orders: number;
                                    vat: number;
                                    expected?: number;
                                    invoiced: number;
                                    difference?: number;
                                    reason?: string;
                                    status: string;
                                    deductions?: {
                                        category: string;
                                        amount: number;
                                        level: string;
                                        expenseAccount?: string;
                                        riderId?: string;
                                    }[];
                                }[];
                            }[];
                            canCommit: boolean;
                            fingerprint: string;
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read a scoped Ninja invoice with available server actions */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}/lines": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read invoiced Ninja entitlements and expected differences */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            invoiceId: string;
                            sourceRow: number;
                            platformAccountId?: string;
                            captainId?: string;
                            lineType?: string;
                            baseGross: number;
                            incentives: number;
                            deductionAmount: number;
                            orders: number;
                            vat: number;
                            expected?: number;
                            invoiced: number;
                            difference?: number;
                            reason?: string;
                            status: string;
                            deductions?: {
                                category: string;
                                amount: number;
                                level: string;
                                expenseAccount?: string;
                                riderId?: string;
                            }[];
                        }[];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}/classify": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Classify an immutable Ninja deduction as company or account level */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}/classify-batch": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Classify many Ninja deductions in one transaction */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve invoiced Ninja facts; expected differences never block */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}/dispute": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Record an explicit formal Ninja dispute; expected differences do not create one */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}/resolve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently resolve a formal Ninja dispute without changing invoiced amounts */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/ninja/invoices/{id}/post": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Post an approved Ninja invoice with VAT, dispute and duplicate-cycle guards */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            platform?: string;
                            period: string;
                            reference?: string;
                            amountExclVat: number;
                            deductionsTotal: number;
                            vat: number;
                            approvalStatus: string;
                            status: string;
                            allowedActions: string[];
                            approvalBlockers?: string[];
                            journalEntryId?: string;
                            createdBy?: string;
                            approvedBy?: string;
                            accountingStatus?: string;
                            reversalJournalEntryId?: string;
                            disputes?: {
                                id: string;
                                status: string;
                                reason: string;
                                createdBy: string;
                                resolvedBy?: string;
                            }[];
                            accountIds?: null | string[];
                            lines?: {
                                id: string;
                                invoiceId: string;
                                sourceRow: number;
                                platformAccountId?: string;
                                captainId?: string;
                                lineType?: string;
                                baseGross: number;
                                incentives: number;
                                deductionAmount: number;
                                orders: number;
                                vat: number;
                                expected?: number;
                                invoiced: number;
                                difference?: number;
                                reason?: string;
                                status: string;
                                deductions?: {
                                    category: string;
                                    amount: number;
                                    level: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/keeta/invoices": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List scoped Keeta invoices */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        /** Preview or confirm a signed Keeta supplier XLSX invoice without recalculating final entitlements */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/keeta/invoices/{id}/lines": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read scoped Keeta invoice lines and source classifications */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/keeta/invoices/{id}/classify": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Classify Keeta deductions before independent approval */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/keeta/invoices/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve final Keeta invoice facts */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/keeta/invoices/{id}/dispute": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Open an explicit Keeta invoice dispute */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/keeta/invoices/{id}/resolve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently resolve a Keeta invoice dispute */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/keeta/invoices/{id}/post": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Post a principal Keeta invoice with dispute, period and duplicate-cycle guards */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/operations/import/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Preview actual operators against approved invoice lines using catalog XLSX columns */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/operations/import/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Confirm signed operations review without posting payroll or creating debts */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/operations/imports/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read a scoped saved operations review for a payroll cycle */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/chefz/operations/review": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Read already-pulled Chefz performance and wallet settlements as a cycle review candidate without writing */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/import/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Preview signed company certificates; administrator only */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/companies/import/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Commit signed company certificates with audit; administrator only */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/{code}/posting": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read administrator platform posting gate; absent defaults off */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    code: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndboolean"];
                    };
                };
            };
        };
        put?: never;
        /** Set and audit platform posting gate; administrator only */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    code: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/terms/{id}/reject": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Reject an owned terms draft with a reason and audit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/terms/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Delete an unapproved owned terms draft with audit */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            deleted: boolean;
                            id: string;
                        };
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/schemes/{id}/reject": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Reject an owned schemes draft with a reason and audit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/schemes/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Delete an unapproved owned schemes draft with audit */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            deleted: boolean;
                            id: string;
                        };
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/eligibility/{id}/reject": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Reject an owned eligibility draft with a reason and audit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/eligibility/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Delete an unapproved owned eligibility draft with audit */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            deleted: boolean;
                            id: string;
                        };
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/settings/{id}/reject": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Reject an owned global settings draft with reason and audit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/settings/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Delete an unapproved owned global settings draft with audit */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            deleted: boolean;
                            id: string;
                        };
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/catalog": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read pay framework catalog and unapproved default settings template */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfIEnumerableOfAccountsAndString[]AndString[]AndString[]AndString[]AndJsonObjectAndboolean"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/terms": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read app monthly parent terms and version history */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                kind?: string;
                                app: string;
                                period: string;
                                code?: string;
                                version: number;
                                status: string;
                                data: {
                                    name?: string;
                                    type?: string;
                                    billingModel?: string;
                                    accountingRole?: string;
                                    eligibilitySetId?: string;
                                    parameters?: Record<string, never>;
                                    values?: Record<string, never>;
                                    rules?: {
                                        metric: string;
                                        operator: string;
                                        threshold: number;
                                        effect: {
                                            type: string;
                                        };
                                        included?: boolean;
                                    }[];
                                };
                                createdBy?: string;
                                effectiveFrom?: string;
                                allowedActions?: string[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Append draft version of parent billing terms */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/schemes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read app internal schemes and category matrix versions */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                kind?: string;
                                app: string;
                                period: string;
                                code?: string;
                                version: number;
                                status: string;
                                data: {
                                    name?: string;
                                    type?: string;
                                    billingModel?: string;
                                    accountingRole?: string;
                                    eligibilitySetId?: string;
                                    parameters?: Record<string, never>;
                                    values?: Record<string, never>;
                                    rules?: {
                                        metric: string;
                                        operator: string;
                                        threshold: number;
                                        effect: {
                                            type: string;
                                        };
                                        included?: boolean;
                                    }[];
                                };
                                createdBy?: string;
                                effectiveFrom?: string;
                                allowedActions?: string[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Append draft internal scheme with six explicit category cells */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/eligibility": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read app-owned eligibility rules without cross-app sharing */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                kind?: string;
                                app: string;
                                period: string;
                                code?: string;
                                version: number;
                                status: string;
                                data: {
                                    name?: string;
                                    type?: string;
                                    billingModel?: string;
                                    accountingRole?: string;
                                    eligibilitySetId?: string;
                                    parameters?: Record<string, never>;
                                    values?: Record<string, never>;
                                    rules?: {
                                        metric: string;
                                        operator: string;
                                        threshold: number;
                                        effect: {
                                            type: string;
                                        };
                                        included?: boolean;
                                    }[];
                                };
                                createdBy?: string;
                                effectiveFrom?: string;
                                allowedActions?: string[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Append draft app-only eligibility rule set */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/terms/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve latest parent terms draft */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/schemes/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve latest scheme and validated eligibility reference */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/eligibility/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve app-owned eligibility */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/copy": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Copy approved prior month into new drafts without overwriting approvals */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/settings": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read effective-dated global pay settings versions */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                kind?: string;
                                app: string;
                                period: string;
                                code?: string;
                                version: number;
                                status: string;
                                data: {
                                    name?: string;
                                    type?: string;
                                    billingModel?: string;
                                    accountingRole?: string;
                                    eligibilitySetId?: string;
                                    parameters?: Record<string, never>;
                                    values?: Record<string, never>;
                                    rules?: {
                                        metric: string;
                                        operator: string;
                                        threshold: number;
                                        effect: {
                                            type: string;
                                        };
                                        included?: boolean;
                                    }[];
                                };
                                createdBy?: string;
                                effectiveFrom?: string;
                                allowedActions?: string[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Append draft global values; defaults never auto-approve */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/settings/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve latest effective-dated global settings */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            app: string;
                            period: string;
                            code?: string;
                            version: number;
                            status: string;
                            data: {
                                name?: string;
                                type?: string;
                                billingModel?: string;
                                accountingRole?: string;
                                eligibilitySetId?: string;
                                parameters?: Record<string, never>;
                                values?: Record<string, never>;
                                rules?: {
                                    metric: string;
                                    operator: string;
                                    threshold: number;
                                    effect: {
                                        type: string;
                                    };
                                    included?: boolean;
                                }[];
                            };
                            createdBy?: string;
                            effectiveFrom?: string;
                            allowedActions?: string[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/assignments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read scoped immutable approved form-based scheme choices */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/assignments/import/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Administrator maps approved pre-month form choices to app schemes in a signed preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/apps/{app}/assignments/import/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Store signed historical scheme choices without modifying responses or riders */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    app: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles/{id}/review": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read the one-grid payroll review with items by deduction phase */
        get: {
            parameters: {
                query?: {
                    review?: string;
                    status?: string;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            cycle: {
                                id: string;
                                period: string;
                                companyId: string;
                                stage: number;
                                status: string;
                            };
                            totals: {
                                riders: number;
                                approved: number;
                                pending: number;
                                needsDecision: number;
                                net: number;
                            };
                            rows: {
                                rider: string;
                                name?: string;
                                status: string;
                                review: string;
                                reviewedBy?: null | string;
                                reviewedAt?: null | string;
                                gross?: number;
                                commission?: number;
                                afterCommission?: number;
                                phase1?: number;
                                preliminaryDue?: number;
                                phase2?: number;
                                phase3?: number;
                                net: number;
                                allowedActions?: string[];
                                blockers?: string[];
                                salaryApprovedAt?: string;
                                debtDeductionStatus?: string;
                                items: {
                                    key: string;
                                    phase: number;
                                    order?: number;
                                    labelAr?: string;
                                    amount: number;
                                    editable: boolean;
                                    max?: null | number;
                                    source: string;
                                }[];
                                edits?: Record<string, never>[];
                            }[];
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles/{id}/review/approve-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approve every ready row of the review grid */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            approved: number;
                            skipped: number;
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles/{id}/review/{iqama}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approve, edit an installment or add a salary adjustment for one rider row */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            rider: string;
                            name?: string;
                            status: string;
                            review: string;
                            reviewedBy?: null | string;
                            reviewedAt?: null | string;
                            gross?: number;
                            commission?: number;
                            afterCommission?: number;
                            phase1?: number;
                            preliminaryDue?: number;
                            phase2?: number;
                            phase3?: number;
                            net: number;
                            allowedActions?: string[];
                            blockers?: string[];
                            salaryApprovedAt?: string;
                            debtDeductionStatus?: string;
                            items: {
                                key: string;
                                phase: number;
                                order?: number;
                                labelAr?: string;
                                amount: number;
                                editable: boolean;
                                max?: null | number;
                                source: string;
                            }[];
                            edits?: Record<string, never>[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles/{id}/unrecovered-costs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read Keeta ineligibility operating and fuel costs borne by the company */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            cycleId: string;
                            status: string;
                            total: number;
                            rows: {
                                rider: string;
                                operatingCost: number;
                                fuelCost: number;
                                total: number;
                                accountCode: string;
                                sourceLineIds: string[];
                            }[];
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles/{id}/debt-deductions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Review due debts after salary approval, capped by positive net */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            cycleId: string;
                            stage: number;
                            status: string;
                            rows: {
                                rider: string;
                                salaryNet: number;
                                net: number;
                                status: string;
                                allowedActions: string[];
                                items: {
                                    key: string;
                                    source: string;
                                    category: string;
                                    labelAr: string;
                                    due: number;
                                    max: number;
                                    amount: number;
                                    remainingBalance: number;
                                    accountCode: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        put?: never;
        /** Choose zero or a due installment and independently confirm deductions */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            cycleId: string;
                            stage: number;
                            status: string;
                            rows: {
                                rider: string;
                                salaryNet: number;
                                net: number;
                                status: string;
                                allowedActions: string[];
                                items: {
                                    key: string;
                                    source: string;
                                    category: string;
                                    labelAr: string;
                                    due: number;
                                    max: number;
                                    amount: number;
                                    remainingBalance: number;
                                    accountCode: string;
                                }[];
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read scoped monthly app cycles and stage history */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                companyId: string;
                                period: string;
                                app?: string;
                                stage: number;
                                status: string;
                                allowedActions?: string[];
                                approvalBlockers?: string[];
                                debtDeductionStatus?: string;
                                invoices: {
                                    invoiceId: string;
                                    app: string;
                                    accounts: {
                                        accountId: string;
                                        sourceLineId: string;
                                        baseGross: number;
                                        incentives: number;
                                        orders: number;
                                        riders: {
                                            rider: string;
                                            sourceLineId?: string;
                                            operationSourceIds?: string[];
                                            baseGross: number;
                                            orders: number;
                                            deduction: number;
                                            category: string;
                                            days: number;
                                            metrics: Record<string, never>;
                                        }[];
                                    }[];
                                    deductions: Record<string, never>[];
                                }[];
                                riders?: {
                                    rider: string;
                                    net: number;
                                    carried?: number;
                                    closing?: number;
                                    status: string;
                                    review?: string;
                                    salaryNet?: number;
                                    carryAmount?: number;
                                    carryDebtId?: string;
                                    salaryApprovedAt?: string;
                                    debtDeductionStatus?: string;
                                    blockers?: string[];
                                }[];
                                journalEntryId?: string;
                                history?: Record<string, never>[];
                                deductions?: Record<string, never>[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Review approved invoices and classify company versus account deductions */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            period: string;
                            app?: string;
                            stage: number;
                            status: string;
                            allowedActions?: string[];
                            approvalBlockers?: string[];
                            debtDeductionStatus?: string;
                            invoices: {
                                invoiceId: string;
                                app: string;
                                accounts: {
                                    accountId: string;
                                    sourceLineId: string;
                                    baseGross: number;
                                    incentives: number;
                                    orders: number;
                                    riders: {
                                        rider: string;
                                        sourceLineId?: string;
                                        operationSourceIds?: string[];
                                        baseGross: number;
                                        orders: number;
                                        deduction: number;
                                        category: string;
                                        days: number;
                                        metrics: Record<string, never>;
                                    }[];
                                }[];
                                deductions: Record<string, never>[];
                            }[];
                            riders?: {
                                rider: string;
                                net: number;
                                carried?: number;
                                closing?: number;
                                status: string;
                                review?: string;
                                salaryNet?: number;
                                carryAmount?: number;
                                carryDebtId?: string;
                                salaryApprovedAt?: string;
                                debtDeductionStatus?: string;
                                blockers?: string[];
                            }[];
                            journalEntryId?: string;
                            history?: Record<string, never>[];
                            deductions?: Record<string, never>[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles/{id}/advance": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Advance one gated stage or independently approve target payroll posting */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            period: string;
                            app?: string;
                            stage: number;
                            status: string;
                            allowedActions?: string[];
                            approvalBlockers?: string[];
                            debtDeductionStatus?: string;
                            invoices: {
                                invoiceId: string;
                                app: string;
                                accounts: {
                                    accountId: string;
                                    sourceLineId: string;
                                    baseGross: number;
                                    incentives: number;
                                    orders: number;
                                    riders: {
                                        rider: string;
                                        sourceLineId?: string;
                                        operationSourceIds?: string[];
                                        baseGross: number;
                                        orders: number;
                                        deduction: number;
                                        category: string;
                                        days: number;
                                        metrics: Record<string, never>;
                                    }[];
                                }[];
                                deductions: Record<string, never>[];
                            }[];
                            riders?: {
                                rider: string;
                                net: number;
                                carried?: number;
                                closing?: number;
                                status: string;
                                review?: string;
                                salaryNet?: number;
                                carryAmount?: number;
                                carryDebtId?: string;
                                salaryApprovedAt?: string;
                                debtDeductionStatus?: string;
                                blockers?: string[];
                            }[];
                            journalEntryId?: string;
                            history?: Record<string, never>[];
                            deductions?: Record<string, never>[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/cycles/{id}/lines": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read scoped cycle calculations with formula evidence */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                cycleId: string;
                                rider: string;
                                app: string;
                                period?: string;
                                status: string;
                                platformGross?: number;
                                incentives?: number;
                                gross: number;
                                companyCommission?: number;
                                entitlement?: number;
                                accountDeduction?: number;
                                allocatedVehicleCharge?: number;
                                sources: {
                                    sourceLineId: string;
                                    accountId: string;
                                    operationSourceIds?: string[];
                                    baseGross: number;
                                    incentive?: number;
                                    deduction?: number;
                                }[];
                                lines?: Record<string, never>[];
                                blockers?: string[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/pay/review-draft": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read source-bound Ninja cycle input and actionable blockers without writes */
        get: {
            parameters: {
                query: {
                    app: string;
                    period: string;
                    companyId: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            app: string;
                            period: string;
                            companyId: string;
                            invoices: {
                                invoiceId: string;
                                app: string;
                                accounts: {
                                    accountId: string;
                                    sourceLineId: string;
                                    baseGross: number;
                                    incentives: number;
                                    orders: number;
                                    riders: {
                                        rider: string;
                                        sourceLineId?: string;
                                        operationSourceIds?: string[];
                                        baseGross: number;
                                        orders: number;
                                        deduction: number;
                                        category: string;
                                        days: number;
                                        metrics: Record<string, never>;
                                    }[];
                                }[];
                                deductions: Record<string, never>[];
                            }[];
                            blockers: {
                                code: string;
                                message: string;
                                fixAction: string;
                                sourceLineId: string;
                            }[];
                            unassignedOrders: Record<string, never>[];
                            ridersNotFound: Record<string, never>[];
                            evidence: Record<string, never>[];
                            canCreate: boolean;
                            totals: {
                                gross: number;
                                deductions: number;
                                vat: number;
                            };
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/operations/{kind}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read allowlisted ERP operations with company scope and pagination */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    kind: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/{iqama}/accounts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read actual rider platform assignments and receive/deliver dates */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/statement/rider/{iqama}/monthly": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read scoped monthly rider app cards and explainable net with legacy fallback */
        get: {
            parameters: {
                query: {
                    period: string;
                };
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            header: {
                                rider: string;
                                name: string;
                                period: string;
                                status?: string;
                                ibanMasked?: string;
                                cycles: Record<string, never>[];
                            };
                            source: string;
                            status: string;
                            net: number;
                            carried: number;
                            closing: number;
                            balanceBasis?: string;
                            approvedOnly?: boolean;
                            cards: {
                                app: string;
                                platform?: string;
                                gross: number;
                                earnedGross: number;
                                companyCommission?: number;
                                vehicleCharge?: number;
                                netBeforePersonal?: number;
                                sources?: {
                                    sourceLineId: string;
                                    accountId: string;
                                    operationSourceIds?: string[];
                                    baseGross: number;
                                    incentive?: number;
                                    deduction?: number;
                                }[];
                                explain: {
                                    label: string;
                                    formula: string;
                                    inputs: Record<string, never>;
                                    amount: number;
                                    category: string;
                                    affectsNet?: boolean;
                                }[];
                            }[];
                            deductions: Record<string, never>[];
                            phases?: null | {
                                [key: string]: number;
                            };
                        };
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/invoices/import/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Preview signed HS workbooks with VAT, row totals and period validation */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/invoices/import/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Store signed HS invoice facts and disputes without posting */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/invoices": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Review paginated HS invoices within company and platform scope */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/disputes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Review formal HS disputes within scope */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/allocations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Review explicit HS TGA allocations and revisions */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/facts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Review paginated immutable approved HS facts by allowlisted kind */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/proposed-journals": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read signed invoice proposals or unpostable CR-002 fact mappings */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/invoices/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve signed HS facts or an open-period reversal; gated posting */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/invoices/{id}/dispute": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Raise an audited formal HS line dispute without charging a rider */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/invoices/{id}/resolve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Propose immutable fact correction/reversal or independently record the platform credit or rejection response */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/allocations/{lineId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Propose an explicit TGA company/rider split; no default */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    lineId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/platforms/hs/allocations/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approve latest TGA split with independent finance checker */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/search": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Search up to twenty permitted riders, companies, debts and campaigns */
        get: {
            parameters: {
                query: {
                    q: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["Hit"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/responses": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Merge campaign and custom-form responses into a filtered review inbox */
        get: {
            parameters: {
                query?: {
                    source?: string;
                    status?: string;
                    campaignId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/excel/{kind}/template": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read excel {kind} template */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    kind: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/excel/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit excel export */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/excel/{kind}/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit excel {kind} preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    kind: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ImportPreview"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/excel/{kind}/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit excel {kind} commit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    kind: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/roles-catalog": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read roles catalog */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfRoleDef[]AndPermissionDef[]AndJsonArrayAndAnonymousTypeOfSponsorDef[]AndIEnumerableOfAccountDefAndCategoryMeta[]AndString[]"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read users */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        /** Submit users */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Update a user and invalidate existing sessions */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/{id}/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Legacy alias for updating a user */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/sync-baseline": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit riders sync baseline */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/enrich-missing": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit riders enrich missing */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndintAndint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/data-quality": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read riders data quality */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndbooleanAndintAndintAndintAndintAndintAndintAndint"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/{iqama}/profile-360": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read riders {iqama} profile 360 */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonObjectAndListOfAnonymousTypeOfstringAndstringAndstringAndbooleanAndbooleanAndstringAndstringAndstringAndstringAndstringAndstringAndstringAndintAndintAnddecimalAndintAndintAndListOfJsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/{iqama}/documents": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit riders {iqama} documents */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/{iqama}/documents/{id}/download": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read riders {iqama} documents {id} download */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    iqama: string;
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read debts */
        get: {
            parameters: {
                query?: {
                    sponsorId?: string;
                    category?: string;
                    status?: string;
                    iqama?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            iqama?: string;
                            category?: string;
                            categoryAr?: string;
                            status: string;
                            totalAmount: number;
                            remainingBalance: number;
                            monthlyInstallment?: number;
                            paidAmount?: number;
                            creditorSponsorId?: string;
                        }[] | {
                            items: {
                                id: string;
                                iqama?: string;
                                category?: string;
                                categoryAr?: string;
                                status: string;
                                totalAmount: number;
                                remainingBalance: number;
                                monthlyInstallment?: number;
                                paidAmount?: number;
                                creditorSponsorId?: string;
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Submit debts */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/settlements": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Read debts settlements
         * @deprecated
         */
        get: {
            parameters: {
                query?: {
                    debtId?: string;
                    iqama?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Update debts {id} */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/{id}/collect": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts {id} collect */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonObjectAndJsonObjectAndJsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/{id}/transfer-sponsor": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts {id} transfer sponsor */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/bulk-collect/template": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read debts bulk collect template */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/bulk-collect/preview-file": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts bulk collect preview file */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndintAndintAndJsonArrayAndJsonArrayAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/bulk-collect/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts bulk collect commit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndintAnddecimalAnddecimalAndJsonArray"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/import-opening/template": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read debts import opening template */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/import-opening/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts import opening preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndintAndintAndintAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndbooleanAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAndintAndintAnddecimalAnddecimalAnddecimalAnddecimalAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndstringAndintAnddecimalAnddecimalAndJsonArrayAndJsonArray"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/import-opening/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts import opening commit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndintAnddecimalAnddecimal"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/opening/decide": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approve or reject a batch of opening-balance debts */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders/lookup": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Find riders by iqama, name, phone or platform account for pickers */
        get: {
            parameters: {
                query: {
                    q: string;
                    limit?: number | string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            iqama?: string;
                            name?: string;
                            status?: string;
                            companyId?: string;
                            phone?: string;
                            chips?: string[];
                        }[];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/sessions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List mobile sessions with safe identifiers within the caller's company scope */
        get: {
            parameters: {
                query?: {
                    iqama?: string;
                    status?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/sessions/{id}/revoke": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Revoke a scoped mobile session with a required audited reason */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/sessions/revoke-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Revoke all sessions for a scoped rider, retaining history */
        post: {
            parameters: {
                query: {
                    iqama: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/rider-asks": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Supervisor lists advance asks from currently supervised riders */
        get: {
            parameters: {
                query?: {
                    status?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/rider-asks/{id}/convert": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Supervisor converts a rider ask into a formal advance request */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/advance-requests/rider-asks/{id}/decline": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Supervisor declines a rider ask with a reason shown to the rider */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/sponsors": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance sponsors */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndintAnddecimalAnddecimalAnddecimalAnddecimal"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/accounts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance accounts */
        get: {
            parameters: {
                query?: {
                    sponsorId?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndstringAndstringAndstringAndstringAndString[]AndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAndstring"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/catalog": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance catalog */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfIEnumerableOfSponsorDefAndintAndIEnumerableOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAndString[]AndstringAndIEnumerableOfAccountDefAndJsonArrayAndRoleDef[]AndPermissionDef[]Andboolean"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/journals": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance journals */
        get: {
            parameters: {
                query?: {
                    sponsorId?: string;
                    iqama?: string;
                    accountCode?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/vault-transfer": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance vault transfer */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-fee": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance jahez fee */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/statement/rider/{iqama}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance statement rider {iqama} */
        get: {
            parameters: {
                query?: {
                    sponsorId?: string;
                };
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/statement/rider/{iqama}/simple": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance statement rider {iqama} simple */
        get: {
            parameters: {
                query: {
                    from: string;
                    to: string;
                    sponsorId?: string;
                };
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/approvals": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read approvals */
        get: {
            parameters: {
                query?: {
                    department?: string;
                    status?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/approvals/{id}/decide": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit approvals {id} decide */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/center": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read notifications center */
        get: {
            parameters: {
                query?: {
                    tab?: string;
                    department?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndintAndintAndintAndListOfJsonObject"];
                    };
                };
            };
        };
        put?: never;
        /** Submit notifications center */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/notifications/center/{id}/toggle-todo": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit notifications center {id} toggle todo */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/broadcast": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit rider mobile broadcast */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/rider-mobile/firebase-config": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read rider mobile firebase config */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndbooleanAndstringAndstringAndintAndintAndintAndintAndintAndstring"];
                    };
                };
            };
        };
        put?: never;
        /** Submit rider mobile firebase config */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndstringAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/features/promissory-notes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance features promissory notes */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/promissory-notes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read promissory notes */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndIEnumerableOfJsonObject"];
                    };
                };
            };
        };
        put?: never;
        /** Submit promissory notes */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/system/release-check": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Read system release check
         * @deprecated
         */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndObjectAndJsonElementAndintAndint"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/coa/target": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance coa target */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/trial-balance": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance trial balance */
        get: {
            parameters: {
                query?: {
                    sponsorId?: string;
                    from?: string;
                    to?: string;
                    view?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/income-statement": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance income statement */
        get: {
            parameters: {
                query?: {
                    sponsorId?: string;
                    from?: string;
                    to?: string;
                    view?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/bank-file/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance bank file preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/bank-file/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance bank file export */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/{id}/reconcile-opening/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts {id} reconcile opening preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonArrayAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/debts/{id}/reconcile-opening/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit debts {id} reconcile opening commit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonObjectAndJsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/opening/template": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance opening template */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/opening/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance opening preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonArrayAndstringAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/opening/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance opening commit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/liability-payment": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance liability payment */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/journals/manual": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List scoped manual journal drafts and approvals */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: {
                                id: string;
                                status: string;
                                companyId?: string;
                                date?: string;
                                reference?: string;
                                description?: string;
                                journalEntryId?: string;
                                allowedActions?: string[];
                                lines?: {
                                    accountCode: string;
                                    debit: number;
                                    credit: number;
                                }[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Draft a balanced manual target-chart journal in an open company period */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            status: string;
                            companyId?: string;
                            date?: string;
                            reference?: string;
                            description?: string;
                            journalEntryId?: string;
                            allowedActions?: string[];
                            lines?: {
                                accountCode: string;
                                debit: number;
                                credit: number;
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/journals/manual/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve a manual journal or its reversal */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            status: string;
                            companyId?: string;
                            date?: string;
                            reference?: string;
                            description?: string;
                            journalEntryId?: string;
                            allowedActions?: string[];
                            lines?: {
                                accountCode: string;
                                debit: number;
                                credit: number;
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/journals/manual/{id}/reject": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Reject a manual journal draft with a reason (checker only) */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            status: string;
                            companyId?: string;
                            date?: string;
                            reference?: string;
                            description?: string;
                            journalEntryId?: string;
                            allowedActions?: string[];
                            lines?: {
                                accountCode: string;
                                debit: number;
                                credit: number;
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/journals/manual/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Delete an unposted manual journal draft (author or administrator) */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            status: string;
                            companyId?: string;
                            date?: string;
                            reference?: string;
                            description?: string;
                            journalEntryId?: string;
                            allowedActions?: string[];
                            lines?: {
                                accountCode: string;
                                debit: number;
                                credit: number;
                            }[];
                        };
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/journals/manual/{id}/reverse": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Create a reasoned open-period reversal draft requiring independent approval */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            status: string;
                            companyId?: string;
                            date?: string;
                            reference?: string;
                            description?: string;
                            journalEntryId?: string;
                            allowedActions?: string[];
                            lines?: {
                                accountCode: string;
                                debit: number;
                                credit: number;
                            }[];
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/journals/manual/{id}/attachment": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Download a scoped private manual-journal attachment */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/accounting-adjustments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List scoped structured financial adjustments */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        /** Draft a structured Mada top-up, salary correction or initial administrative charge */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/accounting-adjustments/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Independently approve a structured financial operation after balance and period checks */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/mada/balance": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read company-specific Mada recharge balance without exposing other vaults */
        get: {
            parameters: {
                query: {
                    companyId: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfdecimal"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-transfer": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance jahez transfer */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-platform-settlement": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance jahez platform settlement */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-fee-deposit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance jahez fee deposit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/legacy-cash-allocation/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance legacy cash allocation preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfJsonArrayAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/legacy-cash-allocation/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance legacy cash allocation commit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-handover": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance jahez handover */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-handovers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read finance jahez handovers */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-handovers/{id}/confirm": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance jahez handovers {id} confirm */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/finance/jahez-handovers/{id}/reject": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit finance jahez handovers {id} reject */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/catalog": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read forms catalog */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndJsonNode"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/builder": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read forms builder */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        /** Submit forms builder */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/builder/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Update forms builder {id} */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        post?: never;
        /** Delete a form after confirming its current fingerprint */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndintAndstring"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/builder/{id}/publish": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit forms builder {id} publish */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/builder/{id}/close": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit forms builder {id} close */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/builder/{id}/qr.svg": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read forms builder {id} qr.svg */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/responses/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read forms responses {id} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/analytics/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read forms analytics {id} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/inquiry-access": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit inquiry access */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read me */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/dashboard": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read dashboard */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndintAndintAndintAnddecimalAndIEnumerableOfJsonObject"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/riders": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read riders */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            iqama?: string;
                            name?: string;
                            status?: string;
                            companyId?: string;
                            phone?: string;
                            chips?: string[];
                        }[] | {
                            items: {
                                id: string;
                                iqama?: string;
                                name?: string;
                                status?: string;
                                companyId?: string;
                                phone?: string;
                                chips?: string[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Submit riders */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/policies": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read policies */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        /** Submit policies */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/policies/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit policies {id} approve */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/imports/inspect": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit imports inspect */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndIEnumerableOfAnonymousTypeOfstringAndintAndint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/imports/{id}/sheet": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read imports {id} sheet */
        get: {
            parameters: {
                query: {
                    sheet: string;
                    headerRow?: number | string;
                };
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfIEnumerableOfAnonymousTypeOfintAndstringAndIEnumerableOfListOfstringAndint"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/imports/{id}/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit imports {id} preview */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["Preview"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/imports/{id}/commit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit imports {id} commit */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfintAndstring"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/imports": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Read imports
         * @deprecated
         */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/template.csv": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Read template.csv
         * @deprecated
         */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/adjustments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit adjustments */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/payroll/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read payroll preview */
        get: {
            parameters: {
                query: {
                    period: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/payroll/runs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read payroll runs */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/payroll/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit payroll approve */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/payroll/fingerprint": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Read payroll fingerprint
         * @deprecated
         */
        get: {
            parameters: {
                query: {
                    iqama: string;
                    period: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstring"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ledger": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read ledger */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/ledger/{iqama}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read ledger {iqama} */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    iqama: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfListOfJsonObjectAnddecimal"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/payments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit payments */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/payroll/export.csv": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read payroll export.csv */
        get: {
            parameters: {
                query: {
                    period: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/campaigns": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read campaigns */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            title?: string;
                            status?: string;
                            expiresAt?: string;
                            path?: string;
                            kind?: string;
                        }[] | {
                            items: {
                                id: string;
                                title?: string;
                                status?: string;
                                expiresAt?: string;
                                path?: string;
                                kind?: string;
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                        };
                    };
                };
            };
        };
        put?: never;
        /** Submit campaigns */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            title?: string;
                            status?: string;
                            expiresAt?: string;
                            path?: string;
                            kind?: string;
                        };
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/campaigns/{id}/qr.svg": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read campaigns {id} qr.svg */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/campaigns/{id}/close": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit campaigns {id} close */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfboolean"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/submissions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read submissions */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/submissions/{id}/review": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit submissions {id} review */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/audit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read audit */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JsonObject"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/campaigns/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Update campaigns {id} */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            title?: string;
                            status?: string;
                            expiresAt?: string;
                            path?: string;
                            kind?: string;
                        };
                    };
                };
            };
        };
        post?: never;
        /** Delete a form after confirming its current fingerprint */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            title?: string;
                            status?: string;
                            expiresAt?: string;
                            path?: string;
                            kind?: string;
                        };
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/campaigns/{id}/analytics": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read campaigns {id} analytics */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": unknown;
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/submissions/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Delete submissions {id} */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndstring"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/submissions/bulk-delete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit submissions bulk delete */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/submissions/bulk-approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit submissions bulk approve */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndint"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/campaigns/{id}/delete-preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Preview permanent form deletion */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndintAndstring"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/forms/builder/{id}/delete-preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Preview permanent form deletion */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndstringAndintAndstring"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        AccountDef: {
            code: string;
            nameAr: string;
            accountType: string;
            naturalSide: string;
            parentCode: null | string;
            defaultSponsorId?: null | string;
            /** @default false */
            isVault: boolean;
            /** @default None */
            vaultKind: string;
            /** @default None */
            vaultPurpose: string;
            /** @default Dual */
            allowedDirections: string;
            allowedSectors?: null | string[];
            /** @default  */
            operationalScopeAr: string;
        };
        Accounts: {
            app: string;
            platform: string;
            receivable: string;
            principal: null | string;
            agent: null | string;
        };
        AnonymousTypeOfboolean: {
            ok: boolean;
        };
        AnonymousTypeOfbooleanAndboolean: {
            ok: boolean;
            fcmRegistered: boolean;
        };
        AnonymousTypeOfbooleanAndIEnumerableOfJsonObject: {
            featureEnabled: boolean;
            items: null | components["schemas"]["JsonObject"][];
        };
        AnonymousTypeOfbooleanAndint: {
            ok: boolean;
            /** Format: int32 */
            count: number | string;
        };
        AnonymousTypeOfbooleanAndintAndstring: {
            ok: boolean;
            /** Format: int32 */
            deletedResponses: number | string;
            message: null | string;
        };
        AnonymousTypeOfbooleanAndstring: {
            needsSetup: boolean;
            environment: null | string;
        };
        AnonymousTypeOfbooleanAndstringAndint: {
            ok: boolean;
            id: null | string;
            /** Format: int32 */
            acknowledgedCount: number | string;
        };
        AnonymousTypeOfbooleanAndstringAndstring: {
            ok: boolean;
            projectId: null | string;
            androidPackageName: null | string;
        };
        AnonymousTypeOfdecimal: {
            /** Format: double */
            balance: number | string;
        };
        "AnonymousTypeOfIEnumerableOfAccountsAndString[]AndString[]AndString[]AndString[]AndJsonObjectAndboolean": {
            apps: null | components["schemas"]["Accounts"][];
            schemeTypes: null | string[];
            categories: null | string[];
            operators: null | string[];
            ninjaMetrics: null | string[];
            defaults: components["schemas"]["JsonObject"];
            defaultsAreDraftOnly: boolean;
        };
        AnonymousTypeOfIEnumerableOfAnonymousTypeOfintAndstringAndIEnumerableOfListOfstringAndint: {
            headers: null | components["schemas"]["AnonymousTypeOfintAndstring"][];
            sample: null | string[][];
            /** Format: int32 */
            rowCount: number | string;
        };
        "AnonymousTypeOfIEnumerableOfSponsorDefAndintAndIEnumerableOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAndString[]AndstringAndIEnumerableOfAccountDefAndJsonArrayAndRoleDef[]AndPermissionDef[]Andboolean": {
            sponsors: null | components["schemas"]["SponsorDef"][];
            /** Format: int32 */
            unassignedDebtCount: number | string;
            categories: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAndString[]Andstring"][];
            vaults: null | components["schemas"]["AccountDef"][];
            departments: components["schemas"]["JsonArray"];
            roles: null | components["schemas"]["RoleDef"][];
            permissions: null | components["schemas"]["PermissionDef"][];
            promissoryNotesArchiveEnabled: boolean;
        };
        AnonymousTypeOfint: {
            /** Format: int32 */
            revoked: number | string;
        };
        AnonymousTypeOfintAndbooleanAndintAndintAndintAndintAndintAndintAndint: {
            /** Format: int32 */
            totalRiders: number | string;
            localBaselineAvailable: boolean;
            /** Format: int32 */
            missingPhoneCount: number | string;
            /** Format: int32 */
            missingCityCount: number | string;
            /** Format: int32 */
            missingSponsorCount: number | string;
            /** Format: int32 */
            missingNationalityCount: number | string;
            /** Format: int32 */
            missingPolicyCount: number | string;
            /** Format: int32 */
            missingIbanCount: number | string;
            /** Format: int32 */
            needsReviewCount: number | string;
        };
        AnonymousTypeOfintAndintAnddecimalAnddecimal: {
            /** Format: int32 */
            importedCount: number | string;
            /** Format: int32 */
            autoCreatedRiders: number | string;
            /** Format: double */
            totalRemainingImported: number | string;
            /** Format: double */
            totalCompanyExpenseImported: number | string;
        };
        AnonymousTypeOfintAndintAnddecimalAnddecimalAndJsonArray: {
            /** Format: int32 */
            processedRows: number | string;
            /** Format: int32 */
            settlementsCreated: number | string;
            /** Format: double */
            totalCollectedBatch: number | string;
            /** Format: double */
            totalWaivedBatch: number | string;
            settlements: components["schemas"]["JsonArray"];
        };
        AnonymousTypeOfintAndintAndint: {
            /** Format: int32 */
            enrichedExisting: number | string;
            /** Format: int32 */
            addedFromSources: number | string;
            /** Format: int32 */
            totalRidersNow: number | string;
        };
        AnonymousTypeOfintAndintAndintAndintAnddecimalAndIEnumerableOfJsonObject: {
            /** Format: int32 */
            riders: number | string;
            /** Format: int32 */
            imports: number | string;
            /** Format: int32 */
            pendingResponses: number | string;
            /** Format: int32 */
            approvedRuns: number | string;
            /** Format: double */
            balance: number | string;
            latestAudit: null | components["schemas"]["JsonObject"][];
        };
        AnonymousTypeOfintAndintAndintAndintAndListOfJsonObject: {
            /** Format: int32 */
            unreadCount: number | string;
            /** Format: int32 */
            generalCount: number | string;
            /** Format: int32 */
            approvalsCount: number | string;
            /** Format: int32 */
            todoCount: number | string;
            items: null | components["schemas"]["JsonObject"][];
        };
        AnonymousTypeOfintAndstring: {
            /** Format: int32 */
            count: number | string;
            batchId: null | string;
        };
        AnonymousTypeOfJobsHealthAndbooleanAndstringAndstringAndbooleanAndboolean: {
            jobs: components["schemas"]["JobsHealth"];
            ok: boolean;
            application: null | string;
            version: null | string;
            dataPreservationVerified: boolean;
            releaseCheckpointPresent: boolean;
        };
        "AnonymousTypeOfJsonArrayAndIEnumerableOfAnonymousTypeOfstringAndintAndString[]": {
            departments: components["schemas"]["JsonArray"];
            ranks: null | components["schemas"]["AnonymousTypeOfstringAndint"][];
            operations: null | string[];
        };
        AnonymousTypeOfJsonArrayAndstring: {
            rows: components["schemas"]["JsonArray"];
            fingerprint: null | string;
        };
        AnonymousTypeOfJsonArrayAndstringAndstring: {
            rows: components["schemas"]["JsonArray"];
            fingerprint: null | string;
            message: null | string;
        };
        AnonymousTypeOfJsonObjectAnddecimalAnddecimalAnddecimalAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstringAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndstring: {
            rider: components["schemas"]["JsonObject"];
            /** Format: double */
            totalRemainingDebt: number | string;
            /** Format: double */
            totalPaidDebt: number | string;
            /** Format: double */
            totalWaivedDebt: number | string;
            debts: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstring"][];
            broadcasts: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndstring"][];
        };
        AnonymousTypeOfJsonObjectAndJsonObject: {
            debt: components["schemas"]["JsonObject"];
            reconciliation: components["schemas"]["JsonObject"];
        };
        AnonymousTypeOfJsonObjectAndJsonObjectAndJsonObject: {
            debt: components["schemas"]["JsonObject"];
            settlement: components["schemas"]["JsonObject"];
            journal: components["schemas"]["JsonObject"];
        };
        AnonymousTypeOfJsonObjectAndListOfAnonymousTypeOfstringAndstringAndstringAndbooleanAndbooleanAndstringAndstringAndstringAndstringAndstringAndstringAndstringAndintAndintAnddecimalAndintAndintAndListOfJsonObject: {
            rider: components["schemas"]["JsonObject"];
            documentChecklist: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndbooleanAndbooleanAndstringAndstringAndstringAndstringAndstringAndstringAndstring"][];
            /** Format: int32 */
            documentsCount: number | string;
            /** Format: int32 */
            activeDebtsCount: number | string;
            /** Format: double */
            totalRemainingDebt: number | string;
            /** Format: int32 */
            workRecordsCount: number | string;
            /** Format: int32 */
            totalOrders: number | string;
            submissions: null | components["schemas"]["JsonObject"][];
        };
        AnonymousTypeOfListOfJsonObjectAnddecimal: {
            rows: null | components["schemas"]["JsonObject"][];
            /** Format: double */
            balance: number | string;
        };
        "AnonymousTypeOfRoleDef[]AndPermissionDef[]AndJsonArrayAndAnonymousTypeOfSponsorDef[]AndIEnumerableOfAccountDefAndCategoryMeta[]AndString[]": {
            roles: null | components["schemas"]["RoleDef"][];
            permissions: null | components["schemas"]["PermissionDef"][];
            departments: components["schemas"]["JsonArray"];
            financialScopeCatalog: components["schemas"]["AnonymousTypeOfSponsorDef[]AndIEnumerableOfAccountDefAndCategoryMeta[]AndString[]"];
        };
        "AnonymousTypeOfSponsorDef[]AndIEnumerableOfAccountDefAndCategoryMeta[]AndString[]": {
            sponsors: null | components["schemas"]["SponsorDef"][];
            accounts: null | components["schemas"]["AccountDef"][];
            categories: null | components["schemas"]["CategoryMeta"][];
            platforms: null | string[];
        };
        AnonymousTypeOfstring: {
            fingerprint: null | string;
        };
        AnonymousTypeOfstringAndboolean: {
            code: null | string;
            enabled: boolean;
        };
        AnonymousTypeOfstringAndint: {
            rank: null | string;
            /** Format: int32 */
            level: number | string;
        };
        AnonymousTypeOfstringAndintAndint: {
            name: null | string;
            /** Format: int32 */
            rows: number | string;
            /** Format: int32 */
            columns: number | string;
        };
        AnonymousTypeOfstringAndintAndintAndJsonArrayAndJsonArrayAndstring: {
            filename: null | string;
            /** Format: int32 */
            validCount: number | string;
            /** Format: int32 */
            errorCount: number | string;
            rows: components["schemas"]["JsonArray"];
            errors: components["schemas"]["JsonArray"];
            fingerprint: null | string;
        };
        AnonymousTypeOfstringAndstringAndIEnumerableOfAnonymousTypeOfstringAndintAndint: {
            id: null | string;
            filename: null | string;
            sheets: null | components["schemas"]["AnonymousTypeOfstringAndintAndint"][];
        };
        AnonymousTypeOfstringAndstringAndintAndintAndintAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndbooleanAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAndintAndintAnddecimalAnddecimalAnddecimalAnddecimalAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndstringAndintAnddecimalAnddecimalAndJsonArrayAndJsonArray: {
            filename: null | string;
            fingerprint: null | string;
            /** Format: int32 */
            validCount: number | string;
            /** Format: int32 */
            errorCount: number | string;
            /** Format: int32 */
            newRidersToCreateCount: number | string;
            /** Format: double */
            totalOriginalDebt: number | string;
            /** Format: double */
            totalPriorPaid: number | string;
            /** Format: double */
            totalOpeningRemaining: number | string;
            /** Format: double */
            totalCompanyExpenseShare: number | string;
            /** Format: double */
            totalDebits: number | string;
            /** Format: double */
            totalCredits: number | string;
            isBalanced: boolean;
            summaryBySponsor: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndintAndintAnddecimalAnddecimalAnddecimalAnddecimal"][];
            summaryByCategory: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndstringAndintAnddecimalAnddecimal"][];
            rows: components["schemas"]["JsonArray"];
            errors: components["schemas"]["JsonArray"];
        };
        AnonymousTypeOfstringAndstringAndintAndstring: {
            id: null | string;
            title: null | string;
            /** Format: int32 */
            responseCount: number | string;
            fingerprint: null | string;
        };
        AnonymousTypeOfstringAndstringAndJsonNode: {
            id: null | string;
            titleAr: null | string;
            fields: components["schemas"]["JsonNode"];
        };
        AnonymousTypeOfstringAndstringAndstringAndbooleanAndbooleanAndstringAndstringAndstringAndstringAndstringAndstringAndstring: {
            documentTypeId: null | string;
            code: null | string;
            nameAr: null | string;
            requiresExpiry: boolean;
            isUploaded: boolean;
            documentId: null | string;
            documentNumber: null | string;
            issueDate: null | string;
            expiryDate: null | string;
            fileName: null | string;
            storagePath: null | string;
            status: null | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndObjectAndJsonElementAndintAndint: {
            version: null | string;
            provider: null | string;
            integrity: null | string;
            snapshot: unknown;
            checkpoint: components["schemas"]["JsonElement"];
            /** Format: int32 */
            orphanCampaignResponses: number | string;
            /** Format: int32 */
            orphanDebts: number | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstring: {
            id: null | string;
            category: null | string;
            categoryAr: null | string;
            creditorSponsorNameAr: null | string;
            /** Format: double */
            totalAmount: number | string;
            /** Format: double */
            paidAmount: number | string;
            /** Format: double */
            waivedAmount: number | string;
            /** Format: double */
            remainingBalance: number | string;
            /** Format: double */
            monthlyInstallment: number | string;
            status: null | string;
            effectiveFrom: null | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndstringAndstring: {
            iqama: null | string;
            scope: null | string;
            code: null | string;
            expiresAt: null | string;
            url: null | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndbooleanAndstringAndstringAndintAndintAndintAndintAndintAndstring: {
            projectId: null | string;
            senderId: null | string;
            vapidPublicKey: null | string;
            androidPackageName: null | string;
            googlePlayUrl: null | string;
            hasServiceAccountJson: boolean;
            legacyStoredKey: boolean;
            configured: boolean;
            webMobilePortalUrl: null | string;
            inquiryPortalUrl: null | string;
            /** Format: int32 */
            activeSessionsCount: number | string;
            /** Format: int32 */
            fcmRegisteredDevicesCount: number | string;
            /** Format: int32 */
            outboxSentCount: number | string;
            /** Format: int32 */
            outboxFailedCount: number | string;
            /** Format: int32 */
            outboxPendingCount: number | string;
            updatedAt: null | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndstring: {
            id: null | string;
            category: null | string;
            titleAr: null | string;
            bodyAr: null | string;
            actionUrl: null | string;
            requiresAck: boolean;
            acknowledged: boolean;
            createdAt: null | string;
        };
        "AnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAndString[]Andstring": {
            code: null | string;
            nameAr: null | string;
            sector: null | string;
            expenseAccountCode: null | string;
            receivableAccountCode: null | string;
            /** Format: double */
            defaultReferenceCost: number | string;
            allowedVaultKinds: null | string[];
            requiredPermission: null | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndintAndintAnddecimalAnddecimalAnddecimalAnddecimal: {
            sponsorId: null | string;
            shortCode: null | string;
            nameAr: null | string;
            shortNameAr: null | string;
            unifiedNumber: null | string;
            /** Format: int32 */
            rowsCount: number | string;
            /** Format: int32 */
            uniqueRidersCount: number | string;
            /** Format: double */
            totalOriginalDebt: number | string;
            /** Format: double */
            totalPriorPaid: number | string;
            /** Format: double */
            totalOpeningRemaining: number | string;
            /** Format: double */
            totalCompanyShare: number | string;
        };
        "AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndbooleanAndbooleanAndstringAndstringAndstringAndstringAndString[]AndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAndstring": {
            code: null | string;
            nameAr: null | string;
            accountType: null | string;
            naturalSide: null | string;
            naturalSideAr: null | string;
            parentCode: null | string;
            isGroup: boolean;
            isVault: boolean;
            vaultKind: null | string;
            vaultPurpose: null | string;
            allowedDirections: null | string;
            allowedDirectionsAr: null | string;
            allowedSectors: null | string[];
            operationalScopeAr: null | string;
            defaultSponsorId: null | string;
            defaultSponsorNameAr: null | string;
            /** Format: double */
            debitTotal: number | string;
            /** Format: double */
            creditTotal: number | string;
            /** Format: double */
            netBalance: number | string;
            ruleExplanationAr: null | string;
        };
        "AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndintAndstringAndString[]AndJsonArrayAndstringAndstring": {
            id: null | string;
            title: null | string;
            expiresAt: null | string;
            createdAt: null | string;
            status: null | string;
            kind: null | string;
            /** Format: int32 */
            responseCount: number | string;
            path: null | string;
            fields: null | string[];
            cities: components["schemas"]["JsonArray"];
            publicUrl: null | string;
            qrCodeUrl: null | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndstringAndintAnddecimalAnddecimal: {
            categoryCode: null | string;
            categoryAr: null | string;
            sector: null | string;
            receivableAccountCode: null | string;
            receivableAccountNameAr: null | string;
            expenseAccountCode: null | string;
            expenseAccountNameAr: null | string;
            /** Format: int32 */
            rowsCount: number | string;
            /** Format: double */
            totalOpeningRemaining: number | string;
            /** Format: double */
            totalCompanyShare: number | string;
        };
        AnonymousTypeOfstringAndstringAndstringAndstringAndstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndintAnddecimalAnddecimalAnddecimalAnddecimal: {
            id: null | string;
            sponsorId: null | string;
            status: null | string;
            code: null | string;
            unifiedNumber: null | string;
            commercialReg: null | string;
            nameAr: null | string;
            shortNameAr: null | string;
            /** Format: double */
            vaultBalance: number | string;
            /** Format: double */
            mainVaultBalance: number | string;
            /** Format: double */
            jahezBoxBalance: number | string;
            /** Format: double */
            bankBalance: number | string;
            /** Format: double */
            petroBalance: number | string;
            /** Format: double */
            receivablesBalance: number | string;
            /** Format: double */
            receivablesFromJournals: number | string;
            /** Format: double */
            receivablesReconciliationDifference: number | string;
            /** Format: int32 */
            activeDebtsCount: number | string;
            /** Format: double */
            payablesBalance: number | string;
            /** Format: double */
            expensesTotal: number | string;
            /** Format: double */
            waiversTotal: number | string;
            /** Format: double */
            interCompanyNet: number | string;
        };
        CategoryMeta: {
            code: string;
            nameAr: string;
            sector: string;
            expenseAccountCode: string;
            receivableAccountCode: string;
            /** Format: double */
            defaultReferenceCost: number | string;
            allowedVaultKinds?: null | string[];
        };
        Hit: {
            type: string;
            id: string;
            title: string;
            subtitle: string;
            url: string;
        };
        ImportIssue: {
            /** Format: int32 */
            row: number | string;
            message: string;
        };
        ImportPreview: {
            fingerprint: string;
            rows: components["schemas"]["ImportRow"][];
            errors: components["schemas"]["ImportIssue"][];
        };
        ImportRow: {
            /** Format: int32 */
            row: number | string;
            data: components["schemas"]["JsonObject"];
        };
        JobsHealth: {
            enabled: boolean;
            /** Format: date-time */
            lastErpSync: null | string;
            /** Format: date-time */
            nextErpSync: null | string;
        };
        JobView: {
            id: string;
            enabled: boolean;
            status: string;
            cron?: null | string;
            timeZone?: null | string;
            /** Format: date-time */
            lastRun?: null | string;
            /** Format: date-time */
            nextRun?: null | string;
            lastStatus?: null | string;
        };
        JsonArray: unknown[];
        JsonElement: unknown;
        JsonNode: unknown;
        JsonObject: Record<string, never>;
        PermissionDef: {
            code: string;
            moduleCode: string;
            moduleAr: string;
            nameAr: string;
            descriptionAr: string;
        };
        Preview: {
            rows: components["schemas"]["WorkLine"][];
            errors: string[];
        };
        RoleDef: {
            code: string;
            nameAr: string;
            defaultDepartment: string;
            /** Format: int32 */
            defaultRankLevel: number | string;
            summaryAr: string;
            defaultPermissions: string[];
        };
        SponsorDef: {
            id: string;
            code: string;
            unifiedNumber: string;
            nameAr: string;
            shortNameAr: string;
            commercialReg: string;
        };
        WorkLine: {
            sourceRef: string;
            iqama: string;
            name: string;
            platform: string;
            accountId: string;
            period: string;
            from: string;
            to: string;
            /** Format: int32 */
            orders: number | string;
            /** Format: double */
            platformRevenue: null | number | string;
            /** Format: double */
            riderPay: null | number | string;
            isValid: null | boolean;
            company: string;
            city: string;
            /** @default false */
            datesConfirmed: boolean;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export type operations = Record<string, never>;
