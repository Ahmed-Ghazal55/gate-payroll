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
        /** Read health */
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
    "/api/health": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read health through the API prefix */
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
    "/api/rider-mobile/forms": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read unified form schemas and active campaigns for the authenticated rider app */
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
    "/api/rider-mobile/forms/{token}/submit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit an open form or campaign from inside the rider app using the authenticated session identity */
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
    "/api/rider-mobile/requests": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read own advance asks and two-way service requests with status timeline and replies */
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
        /** Rider submits an advance ask or a service request (annual leave, IBAN update, data update, statement, complaint, appointment) */
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
        /** Rider cancels an open advance ask or service request */
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
    "/api/rider-mobile/requests/{id}/reply": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Rider sends a reply on an existing request */
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
    "/api/rider-mobile/language": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Switch rider mobile session language (ar, en, ur) */
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
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndstring"];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            kind?: string;
                            category?: string;
                            titleAr: string;
                            titleEn?: string;
                            titleUr?: string;
                            bodyAr?: string;
                            bodyEn?: string;
                            bodyUr?: string;
                            targetIqama?: string;
                            actionUrl?: string;
                            requiresAck?: boolean;
                            deliveryChannel?: string;
                            deliveryStatus?: string;
                            sentBy?: string;
                            createdAt: string;
                            ackCount?: number;
                            readCount?: number;
                        }[] | {
                            items: {
                                id: string;
                                kind?: string;
                                category?: string;
                                titleAr: string;
                                titleEn?: string;
                                titleUr?: string;
                                bodyAr?: string;
                                bodyEn?: string;
                                bodyUr?: string;
                                targetIqama?: string;
                                actionUrl?: string;
                                requiresAck?: boolean;
                                deliveryChannel?: string;
                                deliveryStatus?: string;
                                sentBy?: string;
                                createdAt: string;
                                ackCount?: number;
                                readCount?: number;
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
                        "application/json": components["schemas"]["AnonymousTypeOfJsonObjectAndstringAndJsonObjectAnddecimalAnddecimalAnddecimalAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstringAndstringAndstringAndstringAndstringAndListOfJsonObjectAndAnonymousTypeOfintAndintAndintAndint"];
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
    "/api/rider-mobile/broadcasts/{id}/read": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Record rider read receipt for a broadcast or individual notice */
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
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndstringAndbooleanAndstring"];
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
                        "application/json": components["schemas"]["AnonymousTypeOfbooleanAndstringAndbooleanAndstringAndstringAndint"];
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
    "/api/public/evaluations/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Public evaluation schema */
        get: {
            parameters: {
                query?: {
                    city?: string;
                };
                header?: never;
                path: {
                    token: string;
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
                            schema: string;
                            roundId: string;
                            title: string;
                            noticeAr?: string;
                            noticeEn?: string;
                            noticeUr?: string;
                            cities: string[];
                            apps: string[];
                            staff: {
                                id: string;
                                name: string;
                                cities: string[];
                                departments: string[];
                                roles: string[];
                                apps: string[];
                                active?: boolean;
                                excluded?: boolean;
                                pendingRoleApproval?: boolean;
                            }[];
                            identityFields?: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            staffRatingMin?: number;
                            staffRatingMax?: number;
                            appRatingMin?: number;
                            appRatingMax?: number;
                            staffDimensions: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            staffReasons: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            appReasons: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            factors: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            factorMode?: string;
                            editPolicyPending?: boolean;
                            uploadsPolicyPending?: boolean;
                            allowedActions: string[];
                            attachmentLimits?: {
                                imagesPerCard?: number;
                                voicePerCard?: number;
                                voiceSeconds?: number;
                                replyBytes?: number;
                            };
                        };
                    };
                };
            };
        };
        /** Edit public evaluation reply with receipt */
        put: {
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            receipt: string;
                            allowedActions: string[];
                            editPolicyPending: boolean;
                        };
                    };
                };
            };
        };
        /** Submit public evaluation reply */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            receipt: string;
                            allowedActions: string[];
                            editPolicyPending: boolean;
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
    "/api/rider-mobile/evaluations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Open evaluation rounds for signed-in rider */
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
                            title: string;
                            token: string;
                            schema: string;
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
    "/api/rider-mobile/evaluations/{token}/launch": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Issue one-time hosted evaluation launch URL */
        post: {
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            url: string;
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
    "/api/public/evaluations/{token}/session": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Exchange one-time launch code for survey-scoped session */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            accessToken: string;
                            iqama: string;
                            expiresAt: string;
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
    "/api/rider-mobile/evaluations/{token}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Evaluation schema for signed-in rider */
        get: {
            parameters: {
                query?: {
                    city?: string;
                };
                header?: never;
                path: {
                    token: string;
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
                            schema: string;
                            roundId: string;
                            title: string;
                            noticeAr?: string;
                            noticeEn?: string;
                            noticeUr?: string;
                            cities: string[];
                            apps: string[];
                            staff: {
                                id: string;
                                name: string;
                                cities: string[];
                                departments: string[];
                                roles: string[];
                                apps: string[];
                                active?: boolean;
                                excluded?: boolean;
                                pendingRoleApproval?: boolean;
                            }[];
                            identityFields?: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            staffRatingMin?: number;
                            staffRatingMax?: number;
                            appRatingMin?: number;
                            appRatingMax?: number;
                            staffDimensions: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            staffReasons: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            appReasons: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            factors: {
                                id: string;
                                labelAr: string;
                                labelEn: string;
                                labelUr: string;
                            }[];
                            factorMode?: string;
                            editPolicyPending?: boolean;
                            uploadsPolicyPending?: boolean;
                            allowedActions: string[];
                            attachmentLimits?: {
                                imagesPerCard?: number;
                                voicePerCard?: number;
                                voiceSeconds?: number;
                                replyBytes?: number;
                            };
                        };
                    };
                };
            };
        };
        /** Edit evaluation reply as signed-in rider */
        put: {
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            receipt: string;
                            allowedActions: string[];
                            editPolicyPending: boolean;
                        };
                    };
                };
            };
        };
        /** Submit evaluation reply as signed-in rider */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            receipt: string;
                            allowedActions: string[];
                            editPolicyPending: boolean;
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
    "/api/system/erp-sponsor-map": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Administrator-only company-code to ERP-sponsor match flags (no names) */
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
                        "application/json": components["schemas"]["AnonymousTypeOfstringAndboolean"][];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            sponsorId?: string;
                            code: string;
                            nameAr: string;
                            nameEn?: string;
                            nameOnCertificateAr?: string;
                            shortNameAr?: string;
                            unifiedNumber?: string;
                            commercialReg?: string;
                            crNumber?: string;
                            crType?: string;
                            entityType?: string;
                            characteristic?: string;
                            city?: string;
                            address?: string;
                            capitalSar?: number;
                            crVersion?: number;
                            issueDate?: string;
                            issueDateHijri?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            activities?: {
                                isic: string;
                                nameAr: string;
                            }[];
                            activitiesIncomplete?: boolean;
                            sponsoredRidersCount?: number;
                            status: string;
                            createdAt?: string;
                            updatedAt?: string;
                        }[] | {
                            items: {
                                id: string;
                                sponsorId?: string;
                                code: string;
                                nameAr: string;
                                nameEn?: string;
                                nameOnCertificateAr?: string;
                                shortNameAr?: string;
                                unifiedNumber?: string;
                                commercialReg?: string;
                                crNumber?: string;
                                crType?: string;
                                entityType?: string;
                                characteristic?: string;
                                city?: string;
                                address?: string;
                                capitalSar?: number;
                                crVersion?: number;
                                issueDate?: string;
                                issueDateHijri?: string;
                                annualConfirmation?: string;
                                annualConfirmationHijri?: string;
                                activities?: {
                                    isic: string;
                                    nameAr: string;
                                }[];
                                activitiesIncomplete?: boolean;
                                sponsoredRidersCount?: number;
                                status: string;
                                createdAt?: string;
                                updatedAt?: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            sponsorId?: string;
                            code: string;
                            nameAr: string;
                            nameEn?: string;
                            nameOnCertificateAr?: string;
                            shortNameAr?: string;
                            unifiedNumber?: string;
                            commercialReg?: string;
                            crNumber?: string;
                            crType?: string;
                            entityType?: string;
                            characteristic?: string;
                            city?: string;
                            address?: string;
                            capitalSar?: number;
                            crVersion?: number;
                            issueDate?: string;
                            issueDateHijri?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            activities?: {
                                isic: string;
                                nameAr: string;
                            }[];
                            activitiesIncomplete?: boolean;
                            sponsoredRidersCount?: number;
                            status: string;
                            createdAt?: string;
                            updatedAt?: string;
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
    "/api/companies/stats": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Live sponsorships and platform-account counts per company within scope */
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
                                companyId: string;
                                code: string;
                                nameAr: string;
                                shortNameAr: string;
                                unifiedNumber: string;
                                commercialReg: string;
                                status: string;
                                erpLinked: boolean;
                                sponsorships: {
                                    total: number;
                                    riders: number;
                                    admins: number;
                                    active: number;
                                    onLeave: number;
                                };
                                platformAccounts: {
                                    total: number;
                                    active: number;
                                    assigned: number;
                                    byPlatform: {
                                        code: string;
                                        nameAr: string;
                                        total: number;
                                        active: number;
                                        assigned: number;
                                    }[];
                                };
                            }[];
                            totals: {
                                companiesCount: number;
                                sponsoredTotal: number;
                                sponsoredRiders: number;
                                sponsoredAdmins: number;
                                sponsoredActive: number;
                                sponsoredOnLeave: number;
                                platformAccountsTotal: number;
                                platformAccountsActive: number;
                                platformAccountsAssigned: number;
                                byPlatform: {
                                    code: string;
                                    nameAr: string;
                                    total: number;
                                    active: number;
                                    assigned: number;
                                }[];
                            };
                            source: string;
                            asOf: string;
                            balancesAvailable: boolean;
                            balancesNote: string;
                            allowedActions: string[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            kind: string;
                            number?: string;
                            type?: string;
                            annualConfirmation?: string;
                            expiryDate?: string;
                            status: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            sponsorId?: string;
                            code: string;
                            nameAr: string;
                            nameEn?: string;
                            nameOnCertificateAr?: string;
                            shortNameAr?: string;
                            unifiedNumber?: string;
                            commercialReg?: string;
                            crNumber?: string;
                            crType?: string;
                            entityType?: string;
                            characteristic?: string;
                            city?: string;
                            address?: string;
                            capitalSar?: number;
                            crVersion?: number;
                            issueDate?: string;
                            issueDateHijri?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            activities?: {
                                isic: string;
                                nameAr: string;
                            }[];
                            activitiesIncomplete?: boolean;
                            sponsoredRidersCount?: number;
                            status: string;
                            createdAt?: string;
                            updatedAt?: string;
                        };
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            sponsorId?: string;
                            code: string;
                            nameAr: string;
                            nameEn?: string;
                            nameOnCertificateAr?: string;
                            shortNameAr?: string;
                            unifiedNumber?: string;
                            commercialReg?: string;
                            crNumber?: string;
                            crType?: string;
                            entityType?: string;
                            characteristic?: string;
                            city?: string;
                            address?: string;
                            capitalSar?: number;
                            crVersion?: number;
                            issueDate?: string;
                            issueDateHijri?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            activities?: {
                                isic: string;
                                nameAr: string;
                            }[];
                            activitiesIncomplete?: boolean;
                            sponsoredRidersCount?: number;
                            status: string;
                            createdAt?: string;
                            updatedAt?: string;
                        };
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
    "/api/companies/{id}/stats": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Live sponsorships and platform-account counts for one company within scope */
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
                            item: {
                                companyId: string;
                                code: string;
                                nameAr: string;
                                shortNameAr: string;
                                unifiedNumber: string;
                                commercialReg: string;
                                status: string;
                                erpLinked: boolean;
                                sponsorships: {
                                    total: number;
                                    riders: number;
                                    admins: number;
                                    active: number;
                                    onLeave: number;
                                };
                                platformAccounts: {
                                    total: number;
                                    active: number;
                                    assigned: number;
                                    byPlatform: {
                                        code: string;
                                        nameAr: string;
                                        total: number;
                                        active: number;
                                        assigned: number;
                                    }[];
                                };
                            };
                            source: string;
                            asOf: string;
                            balancesAvailable: boolean;
                            balancesNote: string;
                            allowedActions: string[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            company?: string;
                            number: string;
                            city?: string;
                            issueDate?: string;
                            issueDateHijri?: string;
                            expiryDate?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            crVersion?: number;
                            status: string;
                            createdAt?: string;
                        }[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            company?: string;
                            number: string;
                            city?: string;
                            issueDate?: string;
                            issueDateHijri?: string;
                            expiryDate?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            crVersion?: number;
                            status: string;
                            createdAt?: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            company?: string;
                            number: string;
                            city?: string;
                            issueDate?: string;
                            issueDateHijri?: string;
                            expiryDate?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            crVersion?: number;
                            status: string;
                            createdAt?: string;
                        };
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            company?: string;
                            number: string;
                            city?: string;
                            issueDate?: string;
                            issueDateHijri?: string;
                            expiryDate?: string;
                            annualConfirmation?: string;
                            annualConfirmationHijri?: string;
                            crVersion?: number;
                            status: string;
                            createdAt?: string;
                        };
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            type: string;
                            number?: string;
                            expiryDate?: string;
                            fileName?: string;
                            fileRelativePath?: string;
                            file?: string;
                            contentType?: string;
                            status: string;
                            createdAt?: string;
                        }[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            type: string;
                            number?: string;
                            expiryDate?: string;
                            fileName?: string;
                            fileRelativePath?: string;
                            file?: string;
                            contentType?: string;
                            status: string;
                            createdAt?: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            type: string;
                            number?: string;
                            expiryDate?: string;
                            fileName?: string;
                            fileRelativePath?: string;
                            file?: string;
                            contentType?: string;
                            status: string;
                            createdAt?: string;
                        };
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            companyId: string;
                            type: string;
                            number?: string;
                            expiryDate?: string;
                            fileName?: string;
                            fileRelativePath?: string;
                            file?: string;
                            contentType?: string;
                            status: string;
                            createdAt?: string;
                        };
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                        level?: string;
                                        expenseAccount?: string;
                                        riderId?: string;
                                        migrationStatus?: string;
                                        allocations?: {
                                            bearer: string;
                                            riderId?: string;
                                            amount: number;
                                            expenseAccount?: string;
                                        }[];
                                        proposal?: {
                                            bearer: string;
                                            riderId?: string;
                                            amount: number;
                                            expenseAccount?: string;
                                        }[];
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                        level?: string;
                                        expenseAccount?: string;
                                        riderId?: string;
                                        migrationStatus?: string;
                                        allocations?: {
                                            bearer: string;
                                            riderId?: string;
                                            amount: number;
                                            expenseAccount?: string;
                                        }[];
                                        proposal?: {
                                            bearer: string;
                                            riderId?: string;
                                            amount: number;
                                            expenseAccount?: string;
                                        }[];
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
    "/api/platforms/ninja/invoices/{id}/discard": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Discard a pending unused invoice, retaining source rows and audit */
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                level?: string;
                                expenseAccount?: string;
                                riderId?: string;
                                migrationStatus?: string;
                                allocations?: {
                                    bearer: string;
                                    riderId?: string;
                                    amount: number;
                                    expenseAccount?: string;
                                }[];
                                proposal?: {
                                    bearer: string;
                                    riderId?: string;
                                    amount: number;
                                    expenseAccount?: string;
                                }[];
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
        /** Classify a Ninja deduction with bearer allocations (company or rider+) */
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
    "/api/platforms/ninja/invoices/{id}/multi-rider-deductions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Counts and amounts of multi-rider account deductions needing re-class (no names) */
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
                            invoiceId: string;
                            deductionRows: number;
                            deductionAmount: number;
                            multiRiderRows: number;
                            multiRiderAmount: number;
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                        level?: string;
                                        expenseAccount?: string;
                                        riderId?: string;
                                        migrationStatus?: string;
                                        allocations?: {
                                            bearer: string;
                                            riderId?: string;
                                            amount: number;
                                            expenseAccount?: string;
                                        }[];
                                        proposal?: {
                                            bearer: string;
                                            riderId?: string;
                                            amount: number;
                                            expenseAccount?: string;
                                        }[];
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
    "/api/platforms/keeta/invoices/{id}/discard": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Discard a pending unused Keeta invoice, retaining source rows and audit */
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
    "/api/platforms/keeta/invoices/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read scoped Keeta invoice and available actions */
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
                                    level?: string;
                                    expenseAccount?: string;
                                    riderId?: string;
                                    migrationStatus?: string;
                                    allocations?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
                                    proposal?: {
                                        bearer: string;
                                        riderId?: string;
                                        amount: number;
                                        expenseAccount?: string;
                                    }[];
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
                                level?: string;
                                expenseAccount?: string;
                                riderId?: string;
                                migrationStatus?: string;
                                allocations?: {
                                    bearer: string;
                                    riderId?: string;
                                    amount: number;
                                    expenseAccount?: string;
                                }[];
                                proposal?: {
                                    bearer: string;
                                    riderId?: string;
                                    amount: number;
                                    expenseAccount?: string;
                                }[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            rows: Record<string, never>[];
                            fingerprint: string;
                            counts: {
                                company: {
                                    new: number;
                                    changed: number;
                                    unchanged: number;
                                };
                                commercial_registration: {
                                    new: number;
                                    changed: number;
                                    unchanged: number;
                                };
                            };
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
                        "application/json": components["schemas"]["JsonObject"];
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
                        "application/json": components["schemas"]["AnonymousTypeOfIEnumerableOfAccountsAndString[]AndString[]AndString[]AndString[]AndJsonObjectAndbooleanAndJsonArray"];
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
                                    requestedAmount?: number;
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
                                requestedAmount?: number;
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
    "/api/pay/validate-gaps": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Counts-only ValidateRiders gap report (no identities) */
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
                            allowedActions?: string[];
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
                            allowedActions?: string[];
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
    "/api/pay/cycles/{id}/capture-inputs": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Freeze live ERP (or store) ops inputs for this cycle until approval */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                };
                cookie?: never;
            };
            requestBody?: {
                content: {
                    "application/json": null | components["schemas"]["JsonObject"];
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
                            approvalResult?: {
                                approved: number;
                                skipped: number;
                                skippedRows: {
                                    rider: string;
                                    reason: string;
                                }[];
                            };
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
                            inputsCapturedAt?: string;
                            inputsCapturedBy?: string;
                            inputsCaptureCounts?: {
                                [key: string]: number;
                            };
                            capture?: {
                                cycleId: string;
                                capturedAt: string;
                                counts: {
                                    [key: string]: number;
                                };
                            };
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
                                approvalResult?: {
                                    approved: number;
                                    skipped: number;
                                    skippedRows: {
                                        rider: string;
                                        reason: string;
                                    }[];
                                };
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
                                inputsCapturedAt?: string;
                                inputsCapturedBy?: string;
                                inputsCaptureCounts?: {
                                    [key: string]: number;
                                };
                                capture?: {
                                    cycleId: string;
                                    capturedAt: string;
                                    counts: {
                                        [key: string]: number;
                                    };
                                };
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
                            approvalResult?: {
                                approved: number;
                                skipped: number;
                                skippedRows: {
                                    rider: string;
                                    reason: string;
                                }[];
                            };
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
                            inputsCapturedAt?: string;
                            inputsCapturedBy?: string;
                            inputsCaptureCounts?: {
                                [key: string]: number;
                            };
                            capture?: {
                                cycleId: string;
                                capturedAt: string;
                                counts: {
                                    [key: string]: number;
                                };
                            };
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
                            approvalResult?: {
                                approved: number;
                                skipped: number;
                                skippedRows: {
                                    rider: string;
                                    reason: string;
                                }[];
                            };
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
                            inputsCapturedAt?: string;
                            inputsCapturedBy?: string;
                            inputsCaptureCounts?: {
                                [key: string]: number;
                            };
                            capture?: {
                                cycleId: string;
                                capturedAt: string;
                                counts: {
                                    [key: string]: number;
                                };
                            };
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
    "/api/pay/cycles/{id}/pipeline": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** §40 per-rider stage amounts, stage-4 items, and company expense lines */
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
                            period: string;
                            companyId: string;
                            stage: number;
                            status: string;
                            riders: {
                                rider: string;
                                name?: string;
                                stages: Record<string, never>;
                                lines?: {
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
    "/api/pay/shared-riders": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Queue of shared multi-app riders awaiting consolidation (مندوب عمل مشترك) */
        get: {
            parameters: {
                query?: {
                    period?: string;
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
                            rider: string;
                            period: string;
                            status: string;
                            labelAr: string;
                            missingApps?: string[];
                            apps?: Record<string, never>;
                            combinedNet: number;
                            released?: boolean;
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
    "/api/pay/shared-riders/{rider}/{period}/release": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approver releases a held shared rider with a reason */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    rider: string;
                    period: string;
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
                            period: string;
                            status: string;
                            labelAr: string;
                            missingApps?: string[];
                            apps?: Record<string, never>;
                            combinedNet: number;
                            released?: boolean;
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
                            warnings?: {
                                code: string;
                                message: string;
                                sourceLineId: string;
                                erpOrders?: number;
                                invoiceOrders?: number;
                            }[];
                            allocationProposals?: Record<string, never>[];
                            dataThrough?: string;
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
    "/api/operations/cities-summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Per operating city live summary (O3) */
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
                                code?: string;
                                nameAr?: string;
                                nameEn?: string;
                                status?: string;
                                activeRegistrations?: number;
                                registrationNames?: string;
                                vehiclesCars?: number;
                                vehiclesMotorbikes?: number;
                                vehiclesTotal?: number;
                                ridersTotal?: number;
                                appsWorking?: string;
                                accountsTotal?: number;
                                accountsActive?: number;
                                accountsAssigned?: number;
                                byRegistration?: Record<string, never>[];
                                byApp?: Record<string, never>[];
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/cities/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Operating city detail split by registration and app (O3) */
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
                            id?: string;
                            code?: string;
                            nameAr?: string;
                            status?: string;
                            byRegistration?: Record<string, never>[];
                            byApp?: Record<string, never>[];
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/operations/housing": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Housing list with occupancy KPIs (O4) */
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
                                code?: string;
                                nameAr: string;
                                city?: string;
                                cityCode?: string;
                                roomsCount?: number;
                                capacity?: number;
                                residentsTotal?: number;
                                ridersPerRoom?: number;
                                status?: string;
                                supervisor?: null | string;
                                supervisorSource?: string;
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/housing/{id}/rooms": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Housing rooms and residents (O4) */
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
                            id?: string;
                            nameAr?: string;
                            code?: string;
                            status?: string;
                            city?: string;
                            supervisor?: null | string;
                            supervisorSource?: string;
                            rooms?: {
                                id: string;
                                name?: string;
                                capacity?: number;
                                currentOccupancy?: number;
                                residents?: {
                                    employeeId?: string;
                                    nameAr?: string;
                                    iqama?: string;
                                    checkInDate?: string;
                                }[];
                            }[];
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/operations/vehicles/period-report": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Vehicle/rider period report (O5) */
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
                                assetNumber?: string;
                                plateNumberAr?: string;
                                vehicleType?: string;
                                city?: string;
                                riderNameAr?: string;
                                iqama?: string;
                                vehicleId?: string;
                                operatingDays?: number;
                                sparePartsCost?: number;
                                amountToCollect?: number;
                                oilChangesCount?: number;
                                oilChangesCost?: number;
                                oilMileage?: number;
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/vehicles/purchase-invoices": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Vehicle purchase receipts (O5) */
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
                                receiptNumber?: string;
                                supplierInvoiceNumber?: string;
                                invoiceDate?: string;
                                receivedAt?: string;
                                subtotal?: number;
                                taxAmount?: number;
                                totalAmount?: number;
                                status?: string;
                                supplierNameAr?: string;
                                lines?: Record<string, never>[];
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/vehicles/purchase-invoices/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Purchase receipt lines (O5) */
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
                            receiptNumber?: string;
                            lines?: Record<string, never>[];
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/operations/vehicles/oil-changes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Oil changes in period (M9) */
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
                                vehicleId?: string;
                                plateNumberAr?: string;
                                assetNumber?: string;
                                vehicleType?: string;
                                performedAt?: string;
                                odometer?: number;
                                oilLiters?: number;
                                oilCost?: number;
                                filterCost?: number;
                                laborCost?: number;
                                otherCost?: number;
                                totalCost?: number;
                                notes?: string;
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/vehicles/{id}/oil-changes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Oil changes for a vehicle (O5) */
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
                                vehicleId?: string;
                                plateNumberAr?: string;
                                assetNumber?: string;
                                vehicleType?: string;
                                performedAt?: string;
                                odometer?: number;
                                oilLiters?: number;
                                oilCost?: number;
                                filterCost?: number;
                                laborCost?: number;
                                otherCost?: number;
                                totalCost?: number;
                                notes?: string;
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/vehicles/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Vehicle details drawer including VIN (O5) */
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
                            assetNumber?: string;
                            serialNumber?: string;
                            plateNumberAr?: string;
                            plateNumberEn?: string;
                            vin?: string;
                            chassisNumber?: string;
                            vehicleType?: string;
                            modelYear?: string;
                            currentOdometer?: number;
                            status?: string;
                            sponsorNameAr?: string;
                            city?: string;
                            assignmentHistory?: Record<string, never>[];
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/operations/fuel-cards": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Fuel cards and monthly usage (O6) */
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
                                cardNumber?: string;
                                provider?: string;
                                providerNameAr?: string;
                                cardName?: string;
                                cardNotes?: string;
                                plateNumber?: string;
                                riderNameAr?: string;
                                iqama?: string;
                                liters?: number;
                                amountBeforeTax?: number;
                                vat?: number;
                                totalInclVat?: number;
                                transactionCount?: number;
                                fuelType?: string;
                                cap?: number;
                                capSource?: string;
                                status?: string;
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/fuel-cards/assignments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Fuel card hand-over log (O6) */
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
                                cardNumber?: string;
                                provider?: string;
                                providerNameAr?: string;
                                cardName?: string;
                                cardNotes?: string;
                                plateNumber?: string;
                                riderNameAr?: string;
                                iqama?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                                assignmentReason?: string;
                                endReason?: string;
                            }[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/operations/cities-summary/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export cities summary */
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
                        "application/json": Record<string, never>;
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
    "/api/operations/housing/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export housing list */
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
                        "application/json": Record<string, never>;
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
    "/api/operations/vehicles/period-report/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export vehicles period report */
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
                        "application/json": Record<string, never>;
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
    "/api/operations/vehicles/oil-changes/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export vehicle oil changes */
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
                        "application/json": Record<string, never>;
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
    "/api/operations/vehicles/purchase-invoices/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export vehicle purchase invoices */
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
                        "application/json": Record<string, never>;
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
    "/api/operations/fuel-cards/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export fuel cards */
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
                        "application/json": Record<string, never>;
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
    "/api/operations/fuel-log/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export fuel card assignment log */
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
                        "application/json": Record<string, never>;
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
    "/api/operations/distinct": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Distinct column values for operations table filters */
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
                            column: string;
                            values: string[];
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
    "/api/pay/apps/ninja/live/daily-performance": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Live Ninja daily performance per account and day with coverage (L1) */
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
                                reportDate: string;
                                captainId: string;
                                platformRiderAccountId: string;
                                externalAccountId: string;
                                accountCode?: string;
                                riderProfileId: string;
                                riderClientAssignmentId?: string;
                                sourceId?: string;
                                sourceRowNumber?: number;
                                orders?: number;
                                hours?: number;
                                ownerUnifiedNumber?: string;
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                            coverage: {
                                requestedFrom: string;
                                requestedTo: string;
                                firstDay: string;
                                lastDay: string;
                                total: number;
                                hasGapAtPeriodEnd: boolean;
                            };
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/pay/apps/ninja/live/daily-deductions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Live Ninja daily deductions per actual rider of each day with coverage (L1) */
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
                                reportDate: string;
                                captainId: string;
                                platformRiderAccountId: string;
                                externalAccountId: string;
                                accountCode?: string;
                                actualRiderProfileId: string;
                                riderClientAssignmentId?: string;
                                sourceId?: string;
                                sourceRowNumber?: number;
                                collectedAmount?: number;
                                deductionsAmount?: number;
                                ownerUnifiedNumber?: string;
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                            coverage: {
                                requestedFrom: string;
                                requestedTo: string;
                                firstDay: string;
                                lastDay: string;
                                total: number;
                                hasGapAtPeriodEnd: boolean;
                            };
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/pay/apps/ninja/live/payment-methods": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Live Ninja account payment method / pay scheme from Omar (L1 / U-W2-14) */
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
                                platformRiderAccountId: string;
                                externalAccountId: string;
                                accountCode?: string;
                                accountStatus?: string;
                                accountPaymentModel?: string;
                                assignmentPaymentModel?: string;
                                payScheme: string;
                                ownerEmployeeId?: string;
                                ownerUnifiedNumber: string;
                                openAssignmentId?: string;
                                openActualRiderProfileId?: string;
                                ninjaRiderType?: string;
                                openEffectiveFrom?: string;
                                openEffectiveTo?: string;
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                            coverage: {
                                total: number;
                            };
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/pay/apps/ninja/live/account-bearers": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Live Ninja account owner vs actual rider periods with holder dates (L1 / U-W2-12/13) */
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
                                assignmentId: string;
                                platformRiderAccountId: string;
                                externalAccountId: string;
                                accountCode?: string;
                                owningSponsorId?: string;
                                owningUnifiedNumber: string;
                                ownerEmployeeId: string;
                                ownerRiderProfileId?: string;
                                actualRiderProfileId: string;
                                holderRiderProfileId?: string;
                                effectiveFrom: string;
                                effectiveTo?: string;
                                assignmentPaymentModel?: string;
                                assignmentStatus?: string;
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                            coverage: {
                                total: number;
                                requestedFrom: string;
                                requestedTo: string;
                            };
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/pay/sources/fuel-consumption": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** L1 fuel consumption per rider/card/period (Omar live, facts only) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/pay/sources/vehicle-assignment-periods": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** L1 vehicle assignment periods per rider (Omar live, facts only) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/pay/sources/housing-periods": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** L1 housing residence periods per rider (Omar live, facts only) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/pay/sources/keeta-eligibility": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Keeta expected eligibility result per user/period (Omar live; not approval) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
    "/api/pay/sources/hs-stamp-queue": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** HungerStation WalletPending queue and stamped financial events (read side) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            summary?: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
                            mode?: string;
                            from?: string;
                            to?: string;
                            month?: string;
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
        /** Read allowlisted ERP operations (live from logistics ERP on hosted) with company scope and pagination */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: ({
                                id: string;
                                platformAccountId?: string;
                                companyId?: string;
                                registeredRiderId?: string;
                                reportIdNumber?: string;
                                accountType?: string;
                                vehicleType?: number;
                                dashboardStatus?: string;
                            } | {
                                id: string;
                                platformAccountId?: string;
                                companyId?: string;
                                previousTypeCode?: string;
                                accountType?: string;
                                effectiveFrom?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                totalValidOffers?: number;
                                acceptedOffers?: number;
                                declinedOffers?: number;
                                missedOffers?: number;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                orderId?: string;
                                deliveryFees?: number;
                                cashCollected?: number;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                periodFrom?: string;
                                periodTo?: string;
                                gross?: number;
                                companyAmount?: number;
                                riderAmount?: number;
                            } | {
                                id: string;
                                code?: string;
                                nameAr?: string;
                                nameEn?: string;
                                name?: string;
                                globalCityId?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                issueNumber?: string;
                                issuedAt?: string;
                                quantity?: number;
                                returnedQuantity?: number;
                                totalCost?: number;
                            } | {
                                id: string;
                                assignmentId?: string;
                                eventTypeCode?: string;
                                occurredAt?: string;
                                direction?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                fuelCardId?: string;
                                month?: string;
                                period?: string;
                                liters?: string;
                                amount?: number;
                                totalAmount?: string;
                                amountBeforeTax?: string;
                                vat?: number;
                                fuelType?: number;
                            } | {
                                id: string;
                                code?: string;
                                nameAr?: string;
                                nameEn?: string;
                                cityId?: string;
                                openedDate?: string;
                                closedDate?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                housingId?: string;
                                roomSourceId?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                            } | {
                                id: string;
                                companyId?: string;
                                platformAccountId?: string;
                                from?: string;
                                to?: string;
                                rate?: number;
                            } | {
                                id: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                count?: number;
                            } | {
                                id: string;
                                companyId?: string;
                                number?: string;
                                personName?: string;
                                personTypeCode?: string;
                                riderId?: string;
                                partyRoleCode?: string;
                                caseDate?: string;
                                caseTime?: string;
                                details?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                orders?: number;
                                hours?: number;
                                CaptainId?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                collectedAmount?: number;
                                amount?: number;
                                CaptainId?: string;
                            } | {
                                id: string;
                                frozen?: boolean;
                                policyVersion?: string;
                                inputRevision?: string;
                                reason?: string;
                                createdAt?: string;
                            } | {
                                id: string;
                                code?: string;
                                externalAccountId?: string;
                                platformCode?: string;
                                registeredRiderId?: string;
                                companyId?: string;
                                cityId?: string;
                                paymentModelCode?: string;
                                startDate?: string;
                                endDate?: string;
                                assignments?: {
                                    riderId?: string;
                                    RawEffectiveFrom?: string;
                                    RawEffectiveTo?: string;
                                    EffectiveFrom?: string;
                                    EffectiveTo?: string;
                                    Status?: string;
                                    IsRealRider?: boolean;
                                }[];
                                relationship?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                orders?: number;
                                hours?: number;
                                collectedAmount?: number;
                                deductionsAmount?: number;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformCode?: string;
                                platformAccountId?: string;
                                accountCode?: string;
                                externalAccountId?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                                endReasonCode?: string;
                                endReason?: string;
                                nextRiderId?: string;
                                paymentModelCode?: string;
                                slot?: string;
                                compensationModel?: string;
                                ninjaRiderTypeCode?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                sponsorId?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                            } | {
                                id: string;
                                assetNumber?: string;
                                serialNumber?: string;
                                plateNumberAr?: string;
                                plateNumberEn?: string;
                                vin?: string;
                                chassisNumber?: string;
                                modelYear?: number;
                                vehicleType?: number;
                                fuelType?: number;
                                ownershipType?: number;
                                currentOdometer?: number;
                                currentOperationalStatus?: string;
                                registrationType?: string;
                                companyId?: string;
                                registeredOwnerCompanyId?: string;
                                cityId?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                vehicleId?: string;
                                startedAt?: string;
                                endedAt?: string;
                                startOdometer?: number;
                                endOdometer?: number;
                                isRealRider?: boolean;
                                permissionStartsOn?: string;
                                permissionEndsOn?: string;
                            } | {
                                id: string;
                                vehicleId?: string;
                                riderId?: string;
                                companyId?: string;
                                type?: string;
                                occurredOn?: string;
                                amount?: number;
                                vat?: number;
                                amountBeforeTax?: string;
                            } | {
                                id: string;
                                vehicleId?: string;
                                companyId?: string;
                                statusCode?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                                reason?: string;
                                reasonCode?: string;
                            } | Record<string, never>)[];
                            total: number;
                            page: number;
                            pageSize: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/riders/{iqama}/accounts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read actual rider platform assignments (live from logistics ERP on hosted) */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: ({
                                id: string;
                                platformAccountId?: string;
                                companyId?: string;
                                registeredRiderId?: string;
                                reportIdNumber?: string;
                                accountType?: string;
                                vehicleType?: number;
                                dashboardStatus?: string;
                            } | {
                                id: string;
                                platformAccountId?: string;
                                companyId?: string;
                                previousTypeCode?: string;
                                accountType?: string;
                                effectiveFrom?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                totalValidOffers?: number;
                                acceptedOffers?: number;
                                declinedOffers?: number;
                                missedOffers?: number;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                orderId?: string;
                                deliveryFees?: number;
                                cashCollected?: number;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                periodFrom?: string;
                                periodTo?: string;
                                gross?: number;
                                companyAmount?: number;
                                riderAmount?: number;
                            } | {
                                id: string;
                                code?: string;
                                nameAr?: string;
                                nameEn?: string;
                                name?: string;
                                globalCityId?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                issueNumber?: string;
                                issuedAt?: string;
                                quantity?: number;
                                returnedQuantity?: number;
                                totalCost?: number;
                            } | {
                                id: string;
                                assignmentId?: string;
                                eventTypeCode?: string;
                                occurredAt?: string;
                                direction?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                fuelCardId?: string;
                                month?: string;
                                period?: string;
                                liters?: string;
                                amount?: number;
                                totalAmount?: string;
                                amountBeforeTax?: string;
                                vat?: number;
                                fuelType?: number;
                            } | {
                                id: string;
                                code?: string;
                                nameAr?: string;
                                nameEn?: string;
                                cityId?: string;
                                openedDate?: string;
                                closedDate?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                housingId?: string;
                                roomSourceId?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                            } | {
                                id: string;
                                companyId?: string;
                                platformAccountId?: string;
                                from?: string;
                                to?: string;
                                rate?: number;
                            } | {
                                id: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                count?: number;
                            } | {
                                id: string;
                                companyId?: string;
                                number?: string;
                                personName?: string;
                                personTypeCode?: string;
                                riderId?: string;
                                partyRoleCode?: string;
                                caseDate?: string;
                                caseTime?: string;
                                details?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                orders?: number;
                                hours?: number;
                                CaptainId?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                collectedAmount?: number;
                                amount?: number;
                                CaptainId?: string;
                            } | {
                                id: string;
                                frozen?: boolean;
                                policyVersion?: string;
                                inputRevision?: string;
                                reason?: string;
                                createdAt?: string;
                            } | {
                                id: string;
                                code?: string;
                                externalAccountId?: string;
                                platformCode?: string;
                                registeredRiderId?: string;
                                companyId?: string;
                                cityId?: string;
                                paymentModelCode?: string;
                                startDate?: string;
                                endDate?: string;
                                assignments?: {
                                    riderId?: string;
                                    RawEffectiveFrom?: string;
                                    RawEffectiveTo?: string;
                                    EffectiveFrom?: string;
                                    EffectiveTo?: string;
                                    Status?: string;
                                    IsRealRider?: boolean;
                                }[];
                                relationship?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformAccountId?: string;
                                date?: string;
                                orders?: number;
                                hours?: number;
                                collectedAmount?: number;
                                deductionsAmount?: number;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                platformCode?: string;
                                platformAccountId?: string;
                                accountCode?: string;
                                externalAccountId?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                                endReasonCode?: string;
                                endReason?: string;
                                nextRiderId?: string;
                                paymentModelCode?: string;
                                slot?: string;
                                compensationModel?: string;
                                ninjaRiderTypeCode?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                companyId?: string;
                                sponsorId?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                            } | {
                                id: string;
                                assetNumber?: string;
                                serialNumber?: string;
                                plateNumberAr?: string;
                                plateNumberEn?: string;
                                vin?: string;
                                chassisNumber?: string;
                                modelYear?: number;
                                vehicleType?: number;
                                fuelType?: number;
                                ownershipType?: number;
                                currentOdometer?: number;
                                currentOperationalStatus?: string;
                                registrationType?: string;
                                companyId?: string;
                                registeredOwnerCompanyId?: string;
                                cityId?: string;
                            } | {
                                id: string;
                                riderId?: string;
                                vehicleId?: string;
                                startedAt?: string;
                                endedAt?: string;
                                startOdometer?: number;
                                endOdometer?: number;
                                isRealRider?: boolean;
                                permissionStartsOn?: string;
                                permissionEndsOn?: string;
                            } | {
                                id: string;
                                vehicleId?: string;
                                riderId?: string;
                                companyId?: string;
                                type?: string;
                                occurredOn?: string;
                                amount?: number;
                                vat?: number;
                                amountBeforeTax?: string;
                            } | {
                                id: string;
                                vehicleId?: string;
                                companyId?: string;
                                statusCode?: string;
                                effectiveFrom?: string;
                                effectiveTo?: string;
                                reason?: string;
                                reasonCode?: string;
                            } | Record<string, never>)[];
                            total: number;
                            page: number;
                            pageSize: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Paged workforce directory (live ERP when configured) */
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
                                iqama: string;
                                name: string;
                                phone?: string;
                                nationality?: string;
                                iban?: string;
                                ibanAlert?: string;
                                ibanSource?: string;
                                engagementType?: string;
                                status?: string;
                                sponsorNameAr?: string;
                                sponsor?: string;
                                platform?: string;
                                platforms?: string;
                                platformWorkId?: string;
                                city?: string;
                                workType?: string;
                                mudadSalary?: string;
                                jobTitle?: string;
                                hasCompanyVehicle?: boolean;
                                companyVehicleType?: string;
                                operationalRole?: string;
                                housing?: string;
                                allowedActions?: string[];
                            }[];
                            total: number;
                            page: number;
                            pageSize: number;
                            summary?: {
                                totalCount: number;
                                sponsoredCount: number;
                                sponsoredRiders: number;
                                sponsoredAdmins: number;
                                sponsoredMissingIban: number;
                                byCompany?: {
                                    sponsorUnified: string;
                                    sponsorNameAr: string;
                                    totalCount: number;
                                }[];
                            };
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** KPI counts for workforce filter cards */
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
                            summary: {
                                totalCount: number;
                                sponsoredCount: number;
                                sponsoredRiders: number;
                                sponsoredAdmins: number;
                                sponsoredMissingIban: number;
                                byCompany?: {
                                    sponsorUnified: string;
                                    sponsorNameAr: string;
                                    totalCount: number;
                                }[];
                            };
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce/distinct": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Distinct column values for workforce table filters */
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
                            column: string;
                            values: string[];
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
    "/api/workforce/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Server-side Excel export of workforce rows (H21) */
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
                        "application/json": Record<string, never>;
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
    "/api/workforce/{id}/360": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Reduced workforce 360 (H6) */
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
                            person: {
                                id: string;
                                iqama: string;
                                name: string;
                                phone?: string;
                                nationality?: string;
                                iban?: string;
                                ibanAlert?: string;
                                ibanSource?: string;
                                engagementType?: string;
                                status?: string;
                                sponsorNameAr?: string;
                                sponsor?: string;
                                platform?: string;
                                platforms?: string;
                                platformWorkId?: string;
                                city?: string;
                                workType?: string;
                                mudadSalary?: string;
                                jobTitle?: string;
                                hasCompanyVehicle?: boolean;
                                companyVehicleType?: string;
                                operationalRole?: string;
                                housing?: string;
                                allowedActions?: string[];
                            };
                            workRecord3Months: Record<string, never>[];
                            financialEvaluation: Record<string, never>;
                            iqamaImage: Record<string, never>;
                            ibanImage?: Record<string, never>;
                            promissoryNotes: Record<string, never>[];
                            platformAccounts: Record<string, never>[];
                            legalCases?: Record<string, never>[];
                            financialPosition: Record<string, never>;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce/saudi-payroll-employees": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Omar GOSI payroll employees (H10) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce/terminated": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Terminated and fleeing people (H11) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce/absence-cases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Absence compliance cases (H11) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce/legal-cases": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** HR legal cases (H11/H20) */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/workforce/data-quality": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Workforce data-quality issues from live or store */
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
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
    "/api/hr-catalogs/{kind}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Live HR catalogs from Omar ERP (H9) */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            items: Record<string, never>[];
                            total: number;
                            page?: number;
                            pageSize?: number;
                            source: string;
                            asOf: string;
                            allowedActions: string[];
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
                            closingMarkerAr?: string;
                            disclaimerAr?: string;
                            shortLines?: Record<string, never>[];
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
    "/api/responses/forms": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List campaigns and builder forms with their response columns */
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
                            source: string;
                            kind: string;
                            title: string;
                            status: string;
                            createdAt?: string;
                            expiresAt?: string;
                            responseCount: number;
                            pendingCount: number;
                            columns: {
                                id: string;
                                labelAr: string;
                                type: string;
                                options?: {
                                    value: string;
                                    labelAr?: string;
                                    labelEn?: string;
                                    labelUr?: string;
                                }[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            campaignId: string;
                            campaignTitle?: string;
                            name?: string;
                            iqama: string;
                            phone?: string;
                            city?: string;
                            workSystem?: string;
                            iban?: string;
                            bank?: string;
                            bankCode?: string;
                            platforms?: string[];
                            status: string;
                            isEdited?: boolean;
                            version?: number;
                            createdAt: string;
                            updatedAt?: string;
                            reviewedAt?: string;
                            reviewNote?: string;
                            history?: Record<string, never>[];
                        }[] | {
                            items: {
                                id: string;
                                campaignId: string;
                                campaignTitle?: string;
                                name?: string;
                                iqama: string;
                                phone?: string;
                                city?: string;
                                workSystem?: string;
                                iban?: string;
                                bank?: string;
                                bankCode?: string;
                                platforms?: string[];
                                status: string;
                                isEdited?: boolean;
                                version?: number;
                                createdAt: string;
                                updatedAt?: string;
                                reviewedAt?: string;
                                reviewNote?: string;
                                history?: Record<string, never>[];
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
                        "application/json": components["schemas"]["AnonymousTypeOfRoleDef[]AndIEnumerableOfPermissionDefAndJsonArrayAndDictionaryOfstringAndJsonNodeAndAnonymousTypeOfSponsorDef[]AndIEnumerableOfAccountDefAndCategoryMeta[]AndString[]"];
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
    "/api/admin/approval-exceptions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Self-approval audit report (dated exceptions and bypass uses) */
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
                            action: string;
                            operation: string;
                            recordId: string;
                            maker: string;
                            actor: string;
                            at: string;
                            approvalExceptionUntil: string;
                            selfApprovedException: boolean;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            username: string;
                            role: string;
                            roleNameAr?: string;
                            roleSummaryAr?: string;
                            department?: string;
                            departmentNameAr?: string;
                            rankLevel?: number;
                            status: string;
                            permissions: string[];
                            mustChangePassword?: boolean;
                            customPermissions?: string[];
                            deniedPermissions?: string[];
                            financialScope?: {
                                sponsorIds: string[];
                                accountCodes: string[];
                                platforms: string[];
                                categoryCodes: string[];
                            };
                            createdAt?: string;
                            lastLoginAt?: string;
                            approvalExceptionUntil: string;
                            approvalExceptionReason: string;
                            allowedActions: string[];
                        }[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            username: string;
                            role: string;
                            roleNameAr?: string;
                            roleSummaryAr?: string;
                            department?: string;
                            departmentNameAr?: string;
                            rankLevel?: number;
                            status: string;
                            permissions: string[];
                            mustChangePassword?: boolean;
                            customPermissions?: string[];
                            deniedPermissions?: string[];
                            financialScope?: {
                                sponsorIds: string[];
                                accountCodes: string[];
                                platforms: string[];
                                categoryCodes: string[];
                            };
                            createdAt?: string;
                            lastLoginAt?: string;
                            approvalExceptionUntil: string;
                            approvalExceptionReason: string;
                            allowedActions: string[];
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
    "/api/users/{id}/reset-password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Set a temporary password the user must change at next login */
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
                            username: string;
                            role: string;
                            roleNameAr?: string;
                            roleSummaryAr?: string;
                            department?: string;
                            departmentNameAr?: string;
                            rankLevel?: number;
                            status: string;
                            permissions: string[];
                            mustChangePassword?: boolean;
                            customPermissions?: string[];
                            deniedPermissions?: string[];
                            financialScope?: {
                                sponsorIds: string[];
                                accountCodes: string[];
                                platforms: string[];
                                categoryCodes: string[];
                            };
                            createdAt?: string;
                            lastLoginAt?: string;
                            approvalExceptionUntil: string;
                            approvalExceptionReason: string;
                            allowedActions: string[];
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
    "/api/me/password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Change my own password */
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
                            username: string;
                            role: string;
                            roleNameAr?: string;
                            roleSummaryAr?: string;
                            department?: string;
                            departmentNameAr?: string;
                            rankLevel?: number;
                            status: string;
                            permissions: string[];
                            mustChangePassword?: boolean;
                            customPermissions?: string[];
                            deniedPermissions?: string[];
                            financialScope?: {
                                sponsorIds: string[];
                                accountCodes: string[];
                                platforms: string[];
                                categoryCodes: string[];
                            };
                            createdAt?: string;
                            lastLoginAt?: string;
                            approvalExceptionUntil: string;
                            approvalExceptionReason: string;
                            allowedActions: string[];
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
    "/api/roles/{code}/permissions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Replace a role template and end its users' sessions */
        put: {
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
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/users/{id}/username": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Rename a login (admin only); old names stay reserved and keep maker-checker history; ends the user's sessions */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            username: string;
                            role: string;
                            roleNameAr?: string;
                            roleSummaryAr?: string;
                            department?: string;
                            departmentNameAr?: string;
                            rankLevel?: number;
                            status: string;
                            permissions: string[];
                            mustChangePassword?: boolean;
                            customPermissions?: string[];
                            deniedPermissions?: string[];
                            financialScope?: {
                                sponsorIds: string[];
                                accountCodes: string[];
                                platforms: string[];
                                categoryCodes: string[];
                            };
                            createdAt?: string;
                            lastLoginAt?: string;
                            approvalExceptionUntil: string;
                            approvalExceptionReason: string;
                            allowedActions: string[];
                        };
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            username: string;
                            role: string;
                            roleNameAr?: string;
                            roleSummaryAr?: string;
                            department?: string;
                            departmentNameAr?: string;
                            rankLevel?: number;
                            status: string;
                            permissions: string[];
                            mustChangePassword?: boolean;
                            customPermissions?: string[];
                            deniedPermissions?: string[];
                            financialScope?: {
                                sponsorIds: string[];
                                accountCodes: string[];
                                platforms: string[];
                                categoryCodes: string[];
                            };
                            createdAt?: string;
                            lastLoginAt?: string;
                            approvalExceptionUntil: string;
                            approvalExceptionReason: string;
                            allowedActions: string[];
                        };
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
    "/api/admin/approval-bypass": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List approval.bypass_maker_checker grants */
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
        /** Grant dated approval.bypass_maker_checker */
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
    "/api/admin/approval-bypass/{id}/revoke": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Revoke an approval bypass grant */
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
        /** Read reduced riders {iqama} profile 360 (H6) */
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
                        "application/json": components["schemas"]["AnonymousTypeOfJsonObjectAndJsonObjectAndIEnumerableOfAnonymousTypeOfstringAndstringAndstringAndintAndstringAndstringAndAnonymousTypeOfstringAndstringAndAnonymousTypeOfstringAndstringAndAnonymousTypeOfstringAndstringAndObject[]AndObject[]AndAnonymousTypeOfintAnddecimalAndstringAndstringAndObject[]AndintAndintAnddecimalAndintAndintAndObject[]AndString[]"];
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
    "/api/debts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read debts (array without page; paged envelope when page is set) */
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
                            category?: string;
                            categoryAr?: string;
                            status: string;
                            totalAmount: number;
                            remainingBalance: number;
                            monthlyInstallment?: number;
                            paidAmount?: number;
                            creditorSponsorId?: string;
                            allowedActions?: string[];
                            approvalBlockers?: string[];
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
                                allowedActions?: string[];
                                approvalBlockers?: string[];
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
                            allowedActions?: string[];
                            approvalBlockers?: string[];
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
    "/api/debts/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Scoped debt totals for stat cards (SQL aggregates) */
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
                            totalCount: number;
                            activeRemainingTotal: number;
                            advanceRemainingTotal: number;
                            pendingOpeningCount: number;
                            pendingOpeningTotal: number;
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
    "/api/debts/distinct": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Distinct column values for debts table filters */
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
                            column: string;
                            values: string[];
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
        /** Read one debt with allowedActions */
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
                            iqama?: string;
                            category?: string;
                            categoryAr?: string;
                            status: string;
                            totalAmount: number;
                            remainingBalance: number;
                            monthlyInstallment?: number;
                            paidAmount?: number;
                            creditorSponsorId?: string;
                            allowedActions?: string[];
                            approvalBlockers?: string[];
                        };
                    };
                };
            };
        };
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
                            allowedActions?: string[];
                            approvalBlockers?: string[];
                        };
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            debt: {
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
                                allowedActions?: string[];
                                approvalBlockers?: string[];
                            };
                            settlement?: Record<string, never>;
                            journal?: Record<string, never>;
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
                            allowedActions?: string[];
                            approvalBlockers?: string[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            iqama: string;
                            riderName?: string;
                            language?: string;
                            status: string;
                            createdAt?: string;
                            lastSeenAt?: string;
                            expiresAtUtc?: string;
                            deviceFingerprint?: string;
                            fcmRegistered?: boolean;
                        }[] | {
                            items: {
                                id: string;
                                iqama: string;
                                riderName?: string;
                                language?: string;
                                status: string;
                                createdAt?: string;
                                lastSeenAt?: string;
                                expiresAtUtc?: string;
                                deviceFingerprint?: string;
                                fcmRegistered?: boolean;
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
    "/api/rider-mobile/service-requests": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List rider service requests within caller scope, hiding confidential complaints from unauthorized staff */
        get: {
            parameters: {
                query?: {
                    requestType?: string;
                    status?: string;
                    iqama?: string;
                    group?: string;
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
                            requestType: string;
                            direction?: string;
                            riderIqama: string;
                            riderName?: string;
                            companyId?: string;
                            titleAr?: string;
                            titleEn?: string;
                            titleUr?: string;
                            reason?: string;
                            status: string;
                            approvalStage?: string;
                            requiresSecondApprover?: boolean;
                            confidential?: boolean;
                            payload?: Record<string, never>;
                            timeline?: {
                                at: string;
                                status: string;
                                labelAr?: string;
                                note?: string;
                                byRole?: string;
                            }[];
                            replies?: {
                                id: string;
                                senderRole: string;
                                senderName?: string;
                                message: string;
                                createdAt?: string;
                            }[];
                            note?: string;
                            createdBy?: string;
                            decidedBy?: string;
                            decidedAt?: string;
                            createdAt: string;
                            updatedAt?: string;
                            allowedActions?: string[];
                        }[] | {
                            items: {
                                id: string;
                                requestType: string;
                                direction?: string;
                                riderIqama: string;
                                riderName?: string;
                                companyId?: string;
                                titleAr?: string;
                                titleEn?: string;
                                titleUr?: string;
                                reason?: string;
                                status: string;
                                approvalStage?: string;
                                requiresSecondApprover?: boolean;
                                confidential?: boolean;
                                payload?: Record<string, never>;
                                timeline?: {
                                    at: string;
                                    status: string;
                                    labelAr?: string;
                                    note?: string;
                                    byRole?: string;
                                }[];
                                replies?: {
                                    id: string;
                                    senderRole: string;
                                    senderName?: string;
                                    message: string;
                                    createdAt?: string;
                                }[];
                                note?: string;
                                createdBy?: string;
                                decidedBy?: string;
                                decidedAt?: string;
                                createdAt: string;
                                updatedAt?: string;
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
        /** Admin or staff sends a two-way service request to a rider */
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
                            requestType: string;
                            direction?: string;
                            riderIqama: string;
                            riderName?: string;
                            companyId?: string;
                            titleAr?: string;
                            titleEn?: string;
                            titleUr?: string;
                            reason?: string;
                            status: string;
                            approvalStage?: string;
                            requiresSecondApprover?: boolean;
                            confidential?: boolean;
                            payload?: Record<string, never>;
                            timeline?: {
                                at: string;
                                status: string;
                                labelAr?: string;
                                note?: string;
                                byRole?: string;
                            }[];
                            replies?: {
                                id: string;
                                senderRole: string;
                                senderName?: string;
                                message: string;
                                createdAt?: string;
                            }[];
                            note?: string;
                            createdBy?: string;
                            decidedBy?: string;
                            decidedAt?: string;
                            createdAt: string;
                            updatedAt?: string;
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
    "/api/rider-mobile/service-requests/distinct": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Distinct column values for rider service requests table filters */
        get: {
            parameters: {
                query?: {
                    requestType?: string;
                    status?: string;
                    iqama?: string;
                    group?: string;
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
                            column: string;
                            values: string[];
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
    "/api/rider-mobile/service-requests/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Counts for the rider-app requests tab within caller scope */
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
                            open: number;
                            annualLeaveOpen: number;
                            appointmentsOpen: number;
                            doneThisMonth: number;
                            total: number;
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
    "/api/rider-mobile/service-requests/{id}/decide": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approve (with second-approver enforcement for IBAN/data), reject, or request info on a rider service request */
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
                            requestType: string;
                            direction?: string;
                            riderIqama: string;
                            riderName?: string;
                            companyId?: string;
                            titleAr?: string;
                            titleEn?: string;
                            titleUr?: string;
                            reason?: string;
                            status: string;
                            approvalStage?: string;
                            requiresSecondApprover?: boolean;
                            confidential?: boolean;
                            payload?: Record<string, never>;
                            timeline?: {
                                at: string;
                                status: string;
                                labelAr?: string;
                                note?: string;
                                byRole?: string;
                            }[];
                            replies?: {
                                id: string;
                                senderRole: string;
                                senderName?: string;
                                message: string;
                                createdAt?: string;
                            }[];
                            note?: string;
                            createdBy?: string;
                            decidedBy?: string;
                            decidedAt?: string;
                            createdAt: string;
                            updatedAt?: string;
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
    "/api/rider-mobile/service-requests/{id}/reply": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Staff replies to a rider service request and notifies the rider via FCM */
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
                            requestType: string;
                            direction?: string;
                            riderIqama: string;
                            riderName?: string;
                            companyId?: string;
                            titleAr?: string;
                            titleEn?: string;
                            titleUr?: string;
                            reason?: string;
                            status: string;
                            approvalStage?: string;
                            requiresSecondApprover?: boolean;
                            confidential?: boolean;
                            payload?: Record<string, never>;
                            timeline?: {
                                at: string;
                                status: string;
                                labelAr?: string;
                                note?: string;
                                byRole?: string;
                            }[];
                            replies?: {
                                id: string;
                                senderRole: string;
                                senderName?: string;
                                message: string;
                                createdAt?: string;
                            }[];
                            note?: string;
                            createdBy?: string;
                            decidedBy?: string;
                            decidedAt?: string;
                            createdAt: string;
                            updatedAt?: string;
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
    "/api/rider-mobile/statements/notify-period": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Enqueue batched statement-ready notifications for riders with approved statements in a period */
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
                            ok: boolean;
                            period: string;
                            notifiedRidersCount: number;
                            enqueuedPushCount: number;
                            skippedAlreadyNotifiedCount: number;
                            batchSize?: number;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            sponsorId?: string;
                            code: string;
                            nameAr?: string;
                            shortNameAr?: string;
                            vaultBalance?: number;
                            mainVaultBalance?: number;
                            bankBalance?: number;
                            allowedActions?: string[];
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
                        "application/json": components["schemas"]["AnonymousTypeOfIEnumerableOfSponsorDefAndintAndIEnumerableOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAndString[]AndstringAndIEnumerableOfAccountDefAndJsonArrayAndRoleDef[]AndIEnumerableOfPermissionDefAndbooleanAndJsonArray"];
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
        /** Submit rider mobile broadcast or individual notice */
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
                            category?: string;
                            titleAr: string;
                            titleEn?: string;
                            titleUr?: string;
                            bodyAr?: string;
                            bodyEn?: string;
                            bodyUr?: string;
                            targetIqama?: string;
                            actionUrl?: string;
                            requiresAck?: boolean;
                            deliveryChannel?: string;
                            deliveryStatus?: string;
                            sentBy?: string;
                            createdAt: string;
                            ackCount?: number;
                            readCount?: number;
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
    "/api/rider-mobile/broadcasts/distinct": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Distinct column values for rider mobile broadcasts table filters */
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
                            column: string;
                            values: string[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            projectId?: string;
                            senderId?: string;
                            androidPackageName?: string;
                            googlePlayUrl?: string;
                            hasServiceAccountJson: boolean;
                            legacyStoredKey?: boolean;
                            configured: boolean;
                            webMobilePortalUrl?: string;
                            inquiryPortalUrl?: string;
                            activeSessionsCount: number;
                            fcmRegisteredDevicesCount: number;
                            outboxSentCount: number;
                            outboxFailedCount: number;
                            outboxPendingCount: number;
                            updatedAt?: string;
                        };
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
    "/api/system/maintenance/rider-indexes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read rider index maintenance status with allowedActions */
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
        /** Start rider index maintenance one index at a time */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            sponsorId: string;
                            from: string;
                            to: string;
                            basis: string;
                            comparisonOnly?: boolean;
                            doNotSend?: boolean;
                            doNotSendMark?: string;
                            rows: {
                                bank?: string;
                                iban: string;
                                amount: number;
                                notes?: string;
                                name?: string;
                                iqama: string;
                                address?: string;
                                errors?: string[];
                            }[];
                            excludedRows?: Record<string, never>[];
                            previewToken: string;
                            expiresInSeconds?: number;
                            canExport: boolean;
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
    "/api/finance/treasury/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Cash and bank balances per company from posting CoA accounts only */
        get: {
            parameters: {
                query?: {
                    companyId?: string;
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
                            companies: {
                                companyId: string;
                                code?: string;
                                nameAr?: string;
                                cash: number;
                                bank: number;
                                total: number;
                            }[];
                            totals: {
                                cash: number;
                                bank: number;
                                total: number;
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
    "/api/finance/jahez-reconciliations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List Jahez reconciliation reports */
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
        /** Build Jahez reconciliation report §37 */
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
    "/api/finance/jahez-reconciliations/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read one Jahez reconciliation report */
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
    "/api/finance/jahez-reconciliations/{id}/submit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Submit Jahez reconciliation for approval */
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
    "/api/finance/jahez-reconciliations/{id}/variance": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Record variance decision for Jahez reconciliation */
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
    "/api/finance/jahez-reconciliations/{id}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Approve Jahez reconciliation (maker-checker) */
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            titleAr: string;
                            status: string;
                            fields?: Record<string, never>[];
                            createdAt?: string;
                            updatedAt?: string;
                            publicUrl?: string;
                        }[] | {
                            items: {
                                id: string;
                                titleAr: string;
                                status: string;
                                fields?: Record<string, never>[];
                                createdAt?: string;
                                updatedAt?: string;
                                publicUrl?: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            campaignId: string;
                            campaignTitle?: string;
                            name?: string;
                            iqama: string;
                            phone?: string;
                            city?: string;
                            workSystem?: string;
                            iban?: string;
                            bank?: string;
                            bankCode?: string;
                            platforms?: string[];
                            status: string;
                            isEdited?: boolean;
                            version?: number;
                            createdAt: string;
                            updatedAt?: string;
                            reviewedAt?: string;
                            reviewNote?: string;
                            history?: Record<string, never>[];
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
    "/api/forms/evaluations/staff": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List evaluation staff cards */
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
                            name: string;
                            cities: string[];
                            departments: string[];
                            roles: string[];
                            apps: string[];
                            active?: boolean;
                            excluded?: boolean;
                            pendingRoleApproval?: boolean;
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
    "/api/forms/evaluations/staff/import": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Import evaluation staff cards */
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
                            imported: number;
                            excluded: number;
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
    "/api/forms/evaluations/rounds": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** List evaluation rounds */
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
                            title: string;
                            status: string;
                            kind?: string;
                            schema?: string;
                            sharingToken: string;
                            staff: {
                                id: string;
                                name: string;
                                cities: string[];
                                departments: string[];
                                roles: string[];
                                apps: string[];
                                active?: boolean;
                                excluded?: boolean;
                                pendingRoleApproval?: boolean;
                            }[];
                            cities: string[];
                            apps: string[];
                            createdAt?: string;
                            updatedAt?: string;
                            schemaVersion?: number;
                            allowedActions: string[];
                        }[];
                    };
                };
            };
        };
        put?: never;
        /** Create evaluation round */
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
                            title: string;
                            status: string;
                            kind?: string;
                            schema?: string;
                            sharingToken: string;
                            staff: {
                                id: string;
                                name: string;
                                cities: string[];
                                departments: string[];
                                roles: string[];
                                apps: string[];
                                active?: boolean;
                                excluded?: boolean;
                                pendingRoleApproval?: boolean;
                            }[];
                            cities: string[];
                            apps: string[];
                            createdAt?: string;
                            updatedAt?: string;
                            schemaVersion?: number;
                            allowedActions: string[];
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
    "/api/forms/evaluations/rounds/{id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Read evaluation round */
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
                            title: string;
                            status: string;
                            kind?: string;
                            schema?: string;
                            sharingToken: string;
                            staff: {
                                id: string;
                                name: string;
                                cities: string[];
                                departments: string[];
                                roles: string[];
                                apps: string[];
                                active?: boolean;
                                excluded?: boolean;
                                pendingRoleApproval?: boolean;
                            }[];
                            cities: string[];
                            apps: string[];
                            createdAt?: string;
                            updatedAt?: string;
                            schemaVersion?: number;
                            allowedActions: string[];
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
    "/api/forms/evaluations/rounds/{id}/{action}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Open, pause or close evaluation round */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: string;
                    action: string;
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
                            title: string;
                            status: string;
                            kind?: string;
                            schema?: string;
                            sharingToken: string;
                            staff: {
                                id: string;
                                name: string;
                                cities: string[];
                                departments: string[];
                                roles: string[];
                                apps: string[];
                                active?: boolean;
                                excluded?: boolean;
                                pendingRoleApproval?: boolean;
                            }[];
                            cities: string[];
                            apps: string[];
                            createdAt?: string;
                            updatedAt?: string;
                            schemaVersion?: number;
                            allowedActions: string[];
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
    "/api/forms/evaluations/rounds/{id}/responses": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Page evaluation replies */
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
                                roundId: string;
                                schemaVersion?: number;
                                iqama: string;
                                name: string;
                                city: string;
                                nationality: string;
                                phone?: string;
                                workApps: string[];
                                companyId?: string;
                                identityVerified?: boolean;
                                staffAnswers: {
                                    staffId?: string;
                                    app?: string;
                                    reason?: string;
                                    ratings?: {
                                        professional?: number;
                                        treatment?: number;
                                        support?: number;
                                        responseSpeed?: number;
                                        problemSolving?: number;
                                    };
                                    overall?: number;
                                    factorRatings?: {
                                        orderVolume?: number;
                                        fairPay?: number;
                                        workingHours?: number;
                                        shiftTimes?: number;
                                        flexibility?: number;
                                        customerService?: number;
                                        appProblems?: number;
                                        worthIt?: number;
                                    };
                                    notes?: string;
                                    complaint?: boolean;
                                    complaintDetails?: string;
                                    attachments?: Record<string, never>[];
                                }[];
                                appAnswers: {
                                    staffId?: string;
                                    app?: string;
                                    reason?: string;
                                    ratings?: {
                                        professional?: number;
                                        treatment?: number;
                                        support?: number;
                                        responseSpeed?: number;
                                        problemSolving?: number;
                                    };
                                    overall?: number;
                                    factorRatings?: {
                                        orderVolume?: number;
                                        fairPay?: number;
                                        workingHours?: number;
                                        shiftTimes?: number;
                                        flexibility?: number;
                                        customerService?: number;
                                        appProblems?: number;
                                        worthIt?: number;
                                    };
                                    notes?: string;
                                    complaint?: boolean;
                                    complaintDetails?: string;
                                    attachments?: Record<string, never>[];
                                }[];
                                createdAt: string;
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
    "/api/forms/evaluations/rounds/{id}/statistics": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Evaluation statistics for charts */
        get: {
            parameters: {
                query?: {
                    city?: string;
                    nationalityId?: string;
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
                            roundId: string;
                            totalResponses: number;
                            byCity: {
                                key: string;
                                app?: string;
                                count?: number;
                                responseCount?: number;
                                complaintCount?: number;
                                ratedCount?: number;
                                skippedCount?: number;
                                average?: null | number;
                                staffRatedCount?: number;
                                appRatedCount?: number;
                                staffAverage?: null | number;
                                appAverage?: null | number;
                                reasons?: {
                                    key: string;
                                    count: number;
                                }[];
                                dimensions?: {
                                    key: string;
                                    count: number;
                                    average: null | number;
                                }[];
                                distribution?: {
                                    stars: number;
                                    count: number;
                                }[];
                            }[];
                            byApp: {
                                key: string;
                                app?: string;
                                count?: number;
                                responseCount?: number;
                                complaintCount?: number;
                                ratedCount?: number;
                                skippedCount?: number;
                                average?: null | number;
                                staffRatedCount?: number;
                                appRatedCount?: number;
                                staffAverage?: null | number;
                                appAverage?: null | number;
                                reasons?: {
                                    key: string;
                                    count: number;
                                }[];
                                dimensions?: {
                                    key: string;
                                    count: number;
                                    average: null | number;
                                }[];
                                distribution?: {
                                    stars: number;
                                    count: number;
                                }[];
                            }[];
                            bySupervisor: {
                                key: string;
                                app?: string;
                                count?: number;
                                responseCount?: number;
                                complaintCount?: number;
                                ratedCount?: number;
                                skippedCount?: number;
                                average?: null | number;
                                staffRatedCount?: number;
                                appRatedCount?: number;
                                staffAverage?: null | number;
                                appAverage?: null | number;
                                reasons?: {
                                    key: string;
                                    count: number;
                                }[];
                                dimensions?: {
                                    key: string;
                                    count: number;
                                    average: null | number;
                                }[];
                                distribution?: {
                                    stars: number;
                                    count: number;
                                }[];
                            }[];
                            byNationality: {
                                key: string;
                                app?: string;
                                count?: number;
                                responseCount?: number;
                                complaintCount?: number;
                                ratedCount?: number;
                                skippedCount?: number;
                                average?: null | number;
                                staffRatedCount?: number;
                                appRatedCount?: number;
                                staffAverage?: null | number;
                                appAverage?: null | number;
                                reasons?: {
                                    key: string;
                                    count: number;
                                }[];
                                dimensions?: {
                                    key: string;
                                    count: number;
                                    average: null | number;
                                }[];
                                distribution?: {
                                    stars: number;
                                    count: number;
                                }[];
                            }[];
                            factorAverages: {
                                key: string;
                                app?: string;
                                count?: number;
                                responseCount?: number;
                                complaintCount?: number;
                                ratedCount?: number;
                                skippedCount?: number;
                                average?: null | number;
                                staffRatedCount?: number;
                                appRatedCount?: number;
                                staffAverage?: null | number;
                                appAverage?: null | number;
                                reasons?: {
                                    key: string;
                                    count: number;
                                }[];
                                dimensions?: {
                                    key: string;
                                    count: number;
                                    average: null | number;
                                }[];
                                distribution?: {
                                    stars: number;
                                    count: number;
                                }[];
                            }[];
                            dailyTrend: {
                                key: string;
                                app?: string;
                                count?: number;
                                responseCount?: number;
                                complaintCount?: number;
                                ratedCount?: number;
                                skippedCount?: number;
                                average?: null | number;
                                staffRatedCount?: number;
                                appRatedCount?: number;
                                staffAverage?: null | number;
                                appAverage?: null | number;
                                reasons?: {
                                    key: string;
                                    count: number;
                                }[];
                                dimensions?: {
                                    key: string;
                                    count: number;
                                    average: null | number;
                                }[];
                                distribution?: {
                                    stars: number;
                                    count: number;
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
    "/api/forms/evaluations/rounds/{id}/statistics/{dimension}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Evaluation statistics by dimension */
        get: {
            parameters: {
                query?: {
                    city?: string;
                    nationalityId?: string;
                };
                header?: never;
                path: {
                    id: string;
                    dimension: string;
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
                            roundId: string;
                            totalResponses: number;
                            items: {
                                key: string;
                                app?: string;
                                count?: number;
                                responseCount?: number;
                                complaintCount?: number;
                                ratedCount?: number;
                                skippedCount?: number;
                                average?: null | number;
                                staffRatedCount?: number;
                                appRatedCount?: number;
                                staffAverage?: null | number;
                                appAverage?: null | number;
                                reasons?: {
                                    key: string;
                                    count: number;
                                }[];
                                dimensions?: {
                                    key: string;
                                    count: number;
                                    average: null | number;
                                }[];
                                distribution?: {
                                    stars: number;
                                    count: number;
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
    "/api/forms/evaluations/rounds/{id}/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Export evaluation replies workbook */
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
    "/api/forms/evaluations/attachments/{attachmentId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Play or download an evaluation attachment */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    attachmentId: string;
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
                            title: string;
                            status: string;
                            kind?: string;
                            schema?: string;
                            sharingToken: string;
                            staff: {
                                id: string;
                                name: string;
                                cities: string[];
                                departments: string[];
                                roles: string[];
                                apps: string[];
                                active?: boolean;
                                excluded?: boolean;
                                pendingRoleApproval?: boolean;
                            }[];
                            cities: string[];
                            apps: string[];
                            createdAt?: string;
                            updatedAt?: string;
                            schemaVersion?: number;
                            allowedActions: string[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            iqama: string;
                            scope: string;
                            code: string;
                            expiresAt: string;
                            url: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            username: string;
                            role: string;
                            roleNameAr?: string;
                            roleSummaryAr?: string;
                            department?: string;
                            departmentNameAr?: string;
                            rankLevel?: number;
                            status: string;
                            permissions: string[];
                            mustChangePassword?: boolean;
                            customPermissions?: string[];
                            deniedPermissions?: string[];
                            financialScope?: {
                                sponsorIds: string[];
                                accountCodes: string[];
                                platforms: string[];
                                categoryCodes: string[];
                            };
                            createdAt?: string;
                            lastLoginAt?: string;
                            approvalExceptionUntil: string;
                            approvalExceptionReason: string;
                            allowedActions: string[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": Record<string, never>;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            iqama: string;
                            period: string;
                            fingerprint?: string;
                            gross?: number;
                            net?: number;
                            blockers?: string[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            iqama: string;
                            period: string;
                            approvedAt?: string;
                            net?: number;
                            status?: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            rows: {
                                id: string;
                                iqama: string;
                                amount: number;
                                kind?: string;
                                period?: string;
                            }[];
                            balance: number;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": Record<string, never>;
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
                            publicUrl?: string;
                            qrCodeUrl?: string;
                            responseCount?: number;
                            companyId?: string;
                            target?: string;
                        }[] | {
                            items: {
                                id: string;
                                title?: string;
                                status?: string;
                                expiresAt?: string;
                                path?: string;
                                kind?: string;
                                publicUrl?: string;
                                qrCodeUrl?: string;
                                responseCount?: number;
                                companyId?: string;
                                target?: string;
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
                            publicUrl?: string;
                            qrCodeUrl?: string;
                            responseCount?: number;
                            companyId?: string;
                            target?: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            campaignId: string;
                            campaignTitle?: string;
                            name?: string;
                            iqama: string;
                            phone?: string;
                            city?: string;
                            workSystem?: string;
                            iban?: string;
                            bank?: string;
                            bankCode?: string;
                            platforms?: string[];
                            status: string;
                            isEdited?: boolean;
                            version?: number;
                            createdAt: string;
                            updatedAt?: string;
                            reviewedAt?: string;
                            reviewNote?: string;
                            history?: Record<string, never>[];
                        }[] | {
                            items: {
                                id: string;
                                campaignId: string;
                                campaignTitle?: string;
                                name?: string;
                                iqama: string;
                                phone?: string;
                                city?: string;
                                workSystem?: string;
                                iban?: string;
                                bank?: string;
                                bankCode?: string;
                                platforms?: string[];
                                status: string;
                                isEdited?: boolean;
                                version?: number;
                                createdAt: string;
                                updatedAt?: string;
                                reviewedAt?: string;
                                reviewNote?: string;
                                history?: Record<string, never>[];
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            campaignId: string;
                            campaignTitle?: string;
                            name?: string;
                            iqama: string;
                            phone?: string;
                            city?: string;
                            workSystem?: string;
                            iban?: string;
                            bank?: string;
                            bankCode?: string;
                            platforms?: string[];
                            status: string;
                            isEdited?: boolean;
                            version?: number;
                            createdAt: string;
                            updatedAt?: string;
                            reviewedAt?: string;
                            reviewNote?: string;
                            history?: Record<string, never>[];
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
                            publicUrl?: string;
                            qrCodeUrl?: string;
                            responseCount?: number;
                            companyId?: string;
                            target?: string;
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
                            publicUrl?: string;
                            qrCodeUrl?: string;
                            responseCount?: number;
                            companyId?: string;
                            target?: string;
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
                /** @description Successful response (stored wire field names) */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": {
                            id: string;
                            campaignId: string;
                            campaignTitle?: string;
                            name?: string;
                            iqama: string;
                            phone?: string;
                            city?: string;
                            workSystem?: string;
                            iban?: string;
                            bank?: string;
                            bankCode?: string;
                            platforms?: string[];
                            status: string;
                            isEdited?: boolean;
                            version?: number;
                            createdAt: string;
                            updatedAt?: string;
                            reviewedAt?: string;
                            reviewNote?: string;
                            history?: Record<string, never>[];
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
        AnonymousTypeOfbooleanAndstringAndbooleanAndstring: {
            ok: boolean;
            id: null | string;
            read: boolean;
            readAt: null | string;
        };
        AnonymousTypeOfbooleanAndstringAndbooleanAndstringAndstringAndint: {
            ok: boolean;
            id: null | string;
            acknowledged: boolean;
            readAt: null | string;
            acknowledgedAt: null | string;
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
        "AnonymousTypeOfIEnumerableOfAccountsAndString[]AndString[]AndString[]AndString[]AndJsonObjectAndbooleanAndJsonArray": {
            apps: null | components["schemas"]["Accounts"][];
            schemeTypes: null | string[];
            categories: null | string[];
            operators: null | string[];
            ninjaMetrics: null | string[];
            defaults: components["schemas"]["JsonObject"];
            defaultsAreDraftOnly: boolean;
            allowedActions: components["schemas"]["JsonArray"];
        };
        "AnonymousTypeOfIEnumerableOfSponsorDefAndintAndIEnumerableOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAndString[]AndstringAndIEnumerableOfAccountDefAndJsonArrayAndRoleDef[]AndIEnumerableOfPermissionDefAndbooleanAndJsonArray": {
            sponsors: null | components["schemas"]["SponsorDef"][];
            /** Format: int32 */
            unassignedDebtCount: number | string;
            categories: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAndString[]Andstring"][];
            vaults: null | components["schemas"]["AccountDef"][];
            departments: components["schemas"]["JsonArray"];
            roles: null | components["schemas"]["RoleDef"][];
            permissions: null | components["schemas"]["PermissionDef"][];
            promissoryNotesArchiveEnabled: boolean;
            allowedActions: components["schemas"]["JsonArray"];
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
        AnonymousTypeOfintAnddecimalAndstringAndstring: {
            /** Format: int32 */
            activeDebtsCount: number | string;
            /** Format: double */
            totalRemainingDebt: number | string;
            statementPath: null | string;
            message: null | string;
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
        AnonymousTypeOfintAndintAndintAndint: {
            /** Format: int32 */
            maxBroadcasts: number | string;
            /** Format: int32 */
            maxDebts: number | string;
            /** Format: int32 */
            readRateLimitPerMin: number | string;
            /** Format: int32 */
            writeRateLimitPerMin: number | string;
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
        AnonymousTypeOfJsonObjectAndJsonObject: {
            debt: components["schemas"]["JsonObject"];
            reconciliation: components["schemas"]["JsonObject"];
        };
        "AnonymousTypeOfJsonObjectAndJsonObjectAndIEnumerableOfAnonymousTypeOfstringAndstringAndstringAndintAndstringAndstringAndAnonymousTypeOfstringAndstringAndAnonymousTypeOfstringAndstringAndAnonymousTypeOfstringAndstringAndObject[]AndObject[]AndAnonymousTypeOfintAnddecimalAndstringAndstringAndObject[]AndintAndintAnddecimalAndintAndintAndObject[]AndString[]": {
            person: components["schemas"]["JsonObject"];
            rider: components["schemas"]["JsonObject"];
            workRecord3Months: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndintAndstringAndstring"][];
            financialEvaluation: components["schemas"]["AnonymousTypeOfstringAndstring"];
            iqamaImage: components["schemas"]["AnonymousTypeOfstringAndstring"];
            ibanImage: components["schemas"]["AnonymousTypeOfstringAndstring"];
            promissoryNotes: null | unknown[];
            platformAccounts: null | unknown[];
            financialPosition: components["schemas"]["AnonymousTypeOfintAnddecimalAndstringAndstring"];
            documentChecklist: null | unknown[];
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
            submissions: null | unknown[];
            allowedActions: null | string[];
        };
        AnonymousTypeOfJsonObjectAndJsonObjectAndJsonObject: {
            debt: components["schemas"]["JsonObject"];
            settlement: components["schemas"]["JsonObject"];
            journal: components["schemas"]["JsonObject"];
        };
        AnonymousTypeOfJsonObjectAndstringAndJsonObjectAnddecimalAnddecimalAnddecimalAndListOfAnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstringAndstringAndstringAndstringAndstringAndListOfJsonObjectAndAnonymousTypeOfintAndintAndintAndint: {
            rider: components["schemas"]["JsonObject"];
            language: null | string;
            latestApprovedStatement: components["schemas"]["JsonObject"];
            /** Format: double */
            totalRemainingDebt: number | string;
            /** Format: double */
            totalPaidDebt: number | string;
            /** Format: double */
            totalWaivedDebt: number | string;
            debts: null | components["schemas"]["AnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstringAndstringAndstringAndstringAndstring"][];
            broadcasts: null | components["schemas"]["JsonObject"][];
            loadLimits: components["schemas"]["AnonymousTypeOfintAndintAndintAndint"];
        };
        AnonymousTypeOfListOfJsonObjectAnddecimal: {
            rows: null | components["schemas"]["JsonObject"][];
            /** Format: double */
            balance: number | string;
        };
        "AnonymousTypeOfRoleDef[]AndIEnumerableOfPermissionDefAndJsonArrayAndDictionaryOfstringAndJsonNodeAndAnonymousTypeOfSponsorDef[]AndIEnumerableOfAccountDefAndCategoryMeta[]AndString[]": {
            roles: null | components["schemas"]["RoleDef"][];
            permissions: null | components["schemas"]["PermissionDef"][];
            departments: components["schemas"]["JsonArray"];
            rolePermissions: null | Record<string, never>;
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
            matched: boolean;
        };
        AnonymousTypeOfstringAndint: {
            rank: null | string;
            /** Format: int32 */
            level: number | string;
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
        AnonymousTypeOfstringAndstring: {
            status: null | string;
            message: null | string;
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
        AnonymousTypeOfstringAndstringAndstringAndintAndstringAndstring: {
            month: null | string;
            platform: null | string;
            workDays: null | string;
            /** Format: int32 */
            orders: number | string;
            netDue: null | string;
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
        AnonymousTypeOfstringAndstringAndstringAndstringAndstring: {
            id: null | string;
            title: null | string;
            token: null | string;
            schema: null | string;
            hostedUrl: null | string;
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
        AnonymousTypeOfstringAndstringAndstringAndstringAndstringAnddecimalAnddecimalAnddecimalAnddecimalAnddecimalAndstringAndstringAndstringAndstringAndstringAndstring: {
            id: null | string;
            category: null | string;
            categoryAr: null | string;
            categoryEn: null | string;
            categoryUr: null | string;
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
            direction: null | string;
            markerAr: null | string;
            markerEn: null | string;
            markerUr: null | string;
            status: null | string;
            effectiveFrom: null | string;
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
            /** @default false */
            deprecated: boolean;
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
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export type operations = Record<string, never>;
